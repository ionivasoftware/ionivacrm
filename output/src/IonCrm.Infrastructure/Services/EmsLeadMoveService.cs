using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>One lead row and what moves with it.</summary>
public sealed record LeadMoveRow(
    string? LegacyId,
    string CompanyName,
    bool IsDeleted,
    string? Label,
    string Status,
    int ContactHistories,
    int Tasks,
    int Opportunities,
    int Invoices,
    int Contracts,
    /// <summary>Set when the TARGET project already has a live customer with the same
    /// normalized name — informational only, the row still moves as-is.</summary>
    string? NameCollision);

/// <summary>Full report returned by <see cref="EmsLeadMoveService.MoveAsync"/>.</summary>
public sealed record LeadMoveReport(
    bool DryRun,
    int LeadsFound,
    int LeadsMoved,
    int ContactHistoriesMoved,
    int TasksMoved,
    int OpportunitiesMoved,
    int InvoicesMoved,
    int ContractsMoved,
    int NameCollisions,
    List<LeadMoveRow> Leads,
    List<string> Warnings);

/// <summary>
/// One-shot move of the retired EMS project's LEAD records into the Liftdesk project.
///
/// Leads are CRM-local rows that never existed in either SaaS: LegacyId is null (manually
/// created) or starts with <c>PC-</c>. The customer-data migration
/// (<see cref="EmsToLiftdeskMigrationService"/>) deliberately excludes them; this service
/// re-parents them so the sales pipeline continues under the Liftdesk project and the EMS
/// project can be fully retired.
///
/// Per lead (soft-deleted rows included — the archive moves whole):
///   1. <c>Customer.ProjectId</c> is rewritten to the target project. The row itself moves —
///      Label, Status, contact info, everything on it comes along unchanged.
///   2. Every child row (ContactHistories, CustomerTasks, Opportunities, Invoices,
///      CustomerContracts) gets its denormalized <c>ProjectId</c> rewritten too. CustomerId is
///      untouched — children stay on the same customer.
///
/// No merging: a lead whose name matches an existing live customer in the target project moves
/// as its own row and is flagged in the report (<see cref="LeadMoveRow.NameCollision"/>) so a
/// human can merge later if wanted. Idempotent by construction: moved rows are no longer in the
/// source project, so a re-run finds nothing.
///
/// EXECUTE MODE mirrors the migration service: the sync advisory lock is taken BEFORE the
/// snapshot read, and planning + writes share one transaction (no TOCTOU); every DB call uses
/// <see cref="CancellationToken.None"/> past that point. DRY-RUN performs zero writes.
/// </summary>
public sealed class EmsLeadMoveService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmsLeadMoveService> _logger;

    /// <summary>Initialises a new instance of <see cref="EmsLeadMoveService"/>.</summary>
    public EmsLeadMoveService(ApplicationDbContext db, ILogger<EmsLeadMoveService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private sealed class LeadPlan
    {
        public required Customer Lead { get; init; }
        public string? Collision;
        public int Ch; public int Tk; public int Op; public int Inv; public int Con;
    }

    /// <summary>Runs the move (or a read-only preview when <paramref name="dryRun"/> is true).</summary>
    public async Task<LeadMoveReport> MoveAsync(
        Guid sourceProjectId, Guid targetProjectId, bool dryRun, CancellationToken ct)
    {
        if (sourceProjectId == targetProjectId)
            throw new InvalidOperationException("Kaynak ve hedef proje aynı olamaz.");

        if (dryRun)
            return await RunAsync(sourceProjectId, targetProjectId, dryRun: true, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        var lockTaken = false;
        try
        {
            lockTaken = await ExecuteScalarBoolAsync(
                $"SELECT pg_try_advisory_lock({SyncTimerService.AdvisoryLockKey})");
            if (!lockTaken)
                throw new InvalidOperationException(
                    "Sync kilidi alınamadı — senkronizasyon (veya başka bir taşıma) şu anda çalışıyor. Lütfen kısa süre sonra tekrar deneyin.");

            var report = await RunAsync(sourceProjectId, targetProjectId, dryRun: false, CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            return report;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { /* transaction already completed — original exception wins */ }
            throw;
        }
        finally
        {
            if (lockTaken)
            {
                try { await ExecuteScalarBoolAsync($"SELECT pg_advisory_unlock({SyncTimerService.AdvisoryLockKey})"); }
                catch { /* lock dies with the session */ }
            }
        }
    }

    private async Task<LeadMoveReport> RunAsync(
        Guid sourceProjectId, Guid targetProjectId, bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();

        var source = await _db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == sourceProjectId && !p.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Kaynak proje bulunamadı: {sourceProjectId}");
        var target = await _db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == targetProjectId && !p.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Hedef proje bulunamadı: {targetProjectId}");

        // Tracked load — the ProjectId rewrite below goes through EF change tracking so the
        // SaveChanges interceptor stamps UpdatedAt. IgnoreQueryFilters: soft-deleted leads move too.
        var leads = await _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == sourceProjectId
                        && (c.LegacyId == null || c.LegacyId.StartsWith("PC-")))
            .ToListAsync(ct);

        var plans = leads.Select(l => new LeadPlan { Lead = l }).ToList();

        // Name-collision flags: live customers already in the TARGET project with the same
        // normalized name (informational — a human may want to merge those pairs later).
        var targetLive = await _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == targetProjectId && !c.IsDeleted)
            .Select(c => new { c.LegacyId, c.CompanyName })
            .ToListAsync(ct);
        var targetByName = new Dictionary<string, List<string>>();
        foreach (var t in targetLive)
        {
            var key = CompanyNameMatcher.Normalize(t.CompanyName);
            if (key.Length == 0) continue;
            if (!targetByName.TryGetValue(key, out var list))
                targetByName[key] = list = new List<string>();
            list.Add($"{t.LegacyId} ({t.CompanyName})");
        }
        foreach (var p in plans)
        {
            var key = CompanyNameMatcher.Normalize(p.Lead.CompanyName);
            if (key.Length > 0 && targetByName.TryGetValue(key, out var hits))
                p.Collision = string.Join(", ", hits);
        }

        // Child counts (authoritative for dry-run; execute overwrites with real rowcounts).
        var leadIds = leads.Select(l => l.Id).ToList();
        var chC  = await CountByCustomer(_db.ContactHistories.IgnoreQueryFilters().Where(x => leadIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var tkC  = await CountByCustomer(_db.CustomerTasks.IgnoreQueryFilters().Where(x => leadIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var opC  = await CountByCustomer(_db.Opportunities.IgnoreQueryFilters().Where(x => leadIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var invC = await CountByCustomer(_db.Invoices.IgnoreQueryFilters().Where(x => leadIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var conC = await CountByCustomer(_db.CustomerContracts.IgnoreQueryFilters().Where(x => leadIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        foreach (var p in plans)
        {
            p.Ch  = chC.GetValueOrDefault(p.Lead.Id);
            p.Tk  = tkC.GetValueOrDefault(p.Lead.Id);
            p.Op  = opC.GetValueOrDefault(p.Lead.Id);
            p.Inv = invC.GetValueOrDefault(p.Lead.Id);
            p.Con = conC.GetValueOrDefault(p.Lead.Id);
        }

        // ── Execute (caller already holds the transaction + advisory lock) ───
        if (!dryRun && plans.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var p in plans)
            {
                // Children first: rewrite the denormalized ProjectId; CustomerId stays.
                p.Ch  = await _db.Database.ExecuteSqlRawAsync(ChildProjectStatements[0], targetProjectId, now, p.Lead.Id);
                p.Tk  = await _db.Database.ExecuteSqlRawAsync(ChildProjectStatements[1], targetProjectId, now, p.Lead.Id);
                p.Op  = await _db.Database.ExecuteSqlRawAsync(ChildProjectStatements[2], targetProjectId, now, p.Lead.Id);
                p.Inv = await _db.Database.ExecuteSqlRawAsync(ChildProjectStatements[3], targetProjectId, now, p.Lead.Id);
                p.Con = await _db.Database.ExecuteSqlRawAsync(ChildProjectStatements[4], targetProjectId, now, p.Lead.Id);

                p.Lead.ProjectId = targetProjectId;
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        // ── Report ───────────────────────────────────────────────────────────
        var rows = plans
            .OrderBy(p => p.Lead.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Select(p => new LeadMoveRow(
                LegacyId:         p.Lead.LegacyId,
                CompanyName:      p.Lead.CompanyName,
                IsDeleted:        p.Lead.IsDeleted,
                Label:            p.Lead.Label?.ToString(),
                Status:           p.Lead.Status.ToString(),
                ContactHistories: p.Ch,
                Tasks:            p.Tk,
                Opportunities:    p.Op,
                Invoices:         p.Inv,
                Contracts:        p.Con,
                NameCollision:    p.Collision))
            .ToList();

        var collisions = rows.Count(r => r.NameCollision != null);
        if (collisions > 0)
            warnings.Add(
                $"{collisions} lead, hedef projedeki canlı bir müşteriyle aynı ismi taşıyor — satırlar yine de " +
                "taşındı; istenirse sonradan elle birleştirilebilir (rapordaki NameCollision alanına bakın).");

        var report = new LeadMoveReport(
            DryRun:                dryRun,
            LeadsFound:            plans.Count,
            LeadsMoved:            plans.Count,
            ContactHistoriesMoved: plans.Sum(p => p.Ch),
            TasksMoved:            plans.Sum(p => p.Tk),
            OpportunitiesMoved:    plans.Sum(p => p.Op),
            InvoicesMoved:         plans.Sum(p => p.Inv),
            ContractsMoved:        plans.Sum(p => p.Con),
            NameCollisions:        collisions,
            Leads:                 rows,
            Warnings:              warnings);

        _logger.Log(
            dryRun ? LogLevel.Information : LogLevel.Warning,
            "EMS lead move {Mode}: {Source} → {Target}, leads={Leads} ch={Ch} tasks={Tk} opps={Op} inv={Inv} con={Con}.",
            dryRun ? "DRY-RUN" : "EXECUTED", source.Name, target.Name,
            plans.Count, report.ContactHistoriesMoved, report.TasksMoved,
            report.OpportunitiesMoved, report.InvoicesMoved, report.ContractsMoved);

        return report;
    }

    /// <summary>
    /// Fixed ProjectId-rewrite statements for the five child tables (no dynamic SQL).
    /// Parameters: {0} target ProjectId, {1} UpdatedAt, {2} lead CustomerId.
    /// </summary>
    private static readonly string[] ChildProjectStatements =
    {
        @"UPDATE ""ContactHistories""  SET ""ProjectId"" = {0}, ""UpdatedAt"" = {1} WHERE ""CustomerId"" = {2}",
        @"UPDATE ""CustomerTasks""     SET ""ProjectId"" = {0}, ""UpdatedAt"" = {1} WHERE ""CustomerId"" = {2}",
        @"UPDATE ""Opportunities""     SET ""ProjectId"" = {0}, ""UpdatedAt"" = {1} WHERE ""CustomerId"" = {2}",
        @"UPDATE ""Invoices""          SET ""ProjectId"" = {0}, ""UpdatedAt"" = {1} WHERE ""CustomerId"" = {2}",
        @"UPDATE ""CustomerContracts"" SET ""ProjectId"" = {0}, ""UpdatedAt"" = {1} WHERE ""CustomerId"" = {2}",
    };

    private async Task<bool> ExecuteScalarBoolAsync(string sql)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return result is true;
    }

    private static async Task<Dictionary<Guid, int>> CountByCustomer(
        IQueryable<Guid> ids, CancellationToken ct)
        => await ids.GroupBy(x => x).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
}
