using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

// ── Plan (request) shapes — derived from the migration's dry-run/execute reports ──

/// <summary>One retired EMS source row as it was BEFORE the migration.</summary>
public sealed record RollbackSourcePlan(
    string OriginalLegacyId,
    string CompanyName,
    bool WasDeleted);

/// <summary>One migration target group to undo.</summary>
public sealed record RollbackPairPlan(
    string TargetLegacyId,
    List<RollbackSourcePlan> Sources,
    List<string> CopiedFields,
    bool TargetWasSoftDeleted);

/// <summary>Full rollback plan, posted as the request body.</summary>
public sealed record RollbackPlan(
    List<RollbackPairPlan> Pairs,
    /// <summary>
    /// Children CREATED after this UTC instant stay on the target (they were made post-migration
    /// by a user, not moved by it). Children moved by the migration all have earlier CreatedAt.
    /// </summary>
    DateTime? ChildrenCreatedBefore);

// ── Report shapes ────────────────────────────────────────────────────────────

/// <summary>Per-group outcome of the rollback.</summary>
public sealed record RollbackGroupResult(
    string TargetLegacyId,
    string? TargetCompanyName,
    string? RestoredCanonicalLegacyId,
    int SourcesRestored,
    int ContactHistories,
    int Tasks,
    int Opportunities,
    int Invoices,
    int Contracts,
    List<string> ClearedFields,
    bool TargetUndeleted,
    int ZombiesDeleted,
    string? Skipped);

/// <summary>Full report returned by <see cref="EmsMigrationRollbackService.RollbackAsync"/>.</summary>
public sealed record EmsMigrationRollbackReport(
    bool DryRun,
    int GroupsProcessed,
    int GroupsSkipped,
    int SourcesRestored,
    int ChildrenMovedBack,
    int TargetsUndeleted,
    int ZombiesDeleted,
    List<RollbackGroupResult> Groups,
    List<string> Warnings);

/// <summary>
/// Undoes the EMS→Liftdesk data migration performed by <see cref="EmsToLiftdeskMigrationService"/>.
///
/// WHY: the id-preserved assumption turned out false in production — the CRM's EMS mirror was
/// populated from a DIFFERENT id namespace than the prod Liftdesk tenant, so most pairs moved
/// data onto the WRONG company. Everything is recoverable because (a) the migration retired the
/// sources in place as <c>EMSMIGRATED-{id}</c>, (b) the LIFT targets had been freshly reset and
/// owned ZERO children of their own — every child on a target came from the migration, and
/// (c) the migration reports preserve original LegacyIds, pre-migration deletion flags, the
/// copied-field lists and the carry-over deletions. Those report facts arrive here as the
/// <see cref="RollbackPlan"/> body.
///
/// Per plan group:
///   1. Move EVERY child (contact histories, tasks, opportunities, invoices, contracts) off the
///      LIFT target back onto the canonical restored source, rewriting ProjectId back. Children
///      created after <see cref="RollbackPlan.ChildrenCreatedBefore"/> stay put.
///   2. Clear the CRM-only fields the migration copied onto the target (they were empty before).
///   3. Un-delete targets the migration soft-deleted via carry-over.
///   4. Restore each EMSMIGRATED-{id} source: original LegacyId + original IsDeleted flag.
///   5. Delete ZOMBIE duplicates: while the sources sat retired, the still-running EMS sync
///      re-inserted the same companies under their original LegacyIds. Any OTHER live, childless
///      row with a restored (ProjectId, LegacyId) is hard-deleted; a zombie that somehow owns
///      children is left alone and reported.
///
/// Execute runs in one transaction holding the same advisory lock as <see cref="SyncTimerService"/>;
/// past the first write all DB calls use <see cref="CancellationToken.None"/>. Idempotent: a
/// second run finds no EMSMIGRATED sources and skips every group.
/// </summary>
public sealed class EmsMigrationRollbackService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmsMigrationRollbackService> _logger;

    /// <summary>Initialises a new instance of <see cref="EmsMigrationRollbackService"/>.</summary>
    public EmsMigrationRollbackService(ApplicationDbContext db, ILogger<EmsMigrationRollbackService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Runs the rollback (or a read-only preview when <paramref name="dryRun"/> is true).</summary>
    public async Task<EmsMigrationRollbackReport> RollbackAsync(
        RollbackPlan plan, bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();
        var cutoff = plan.ChildrenCreatedBefore ?? DateTime.UtcNow;

        var allCustomers = await _db.Customers.IgnoreQueryFilters().ToListAsync(ct);

        var byLegacy = allCustomers
            .Where(c => c.LegacyId != null)
            .GroupBy(c => c.LegacyId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // ── Build per-group work items ────────────────────────────────────────
        var groups = new List<GroupWork>();
        var skipped = new List<RollbackGroupResult>();

        foreach (var pair in plan.Pairs)
        {
            if (!TryNumericId(pair.TargetLegacyId, "LIFT-", out var numericId))
            {
                skipped.Add(Skip(pair, null, "Hedef LegacyId çözümlenemedi"));
                continue;
            }

            var target = byLegacy.TryGetValue(pair.TargetLegacyId, out var tRows)
                ? tRows.OrderBy(t => t.IsDeleted ? 1 : 0).First()
                : null;
            if (target is null)
            {
                skipped.Add(Skip(pair, null, "Hedef LIFT satırı bulunamadı"));
                continue;
            }

            var marker = $"EMSMIGRATED-{numericId}";
            var sources = byLegacy.TryGetValue(marker, out var sRows) ? sRows : new List<Customer>();
            if (sources.Count == 0)
            {
                skipped.Add(Skip(pair, target.CompanyName, "EMSMIGRATED kaynağı yok (zaten geri alınmış?)"));
                continue;
            }

            // Match DB source rows to plan sources by normalized name; leftovers pair up in order.
            var restoreMap = MatchSources(sources, pair.Sources, warnings, marker);

            // Canonical restored source = the one whose plan says it was LIVE before migration
            // (children belong with the visible row); fall back to the first.
            var canonical = restoreMap.FirstOrDefault(m => !m.Plan.WasDeleted).Db ?? restoreMap[0].Db;

            groups.Add(new GroupWork(pair, numericId, target, restoreMap, canonical));
        }

        // ── Child counts for the report (dry-run authoritative; execute overwrites) ──
        var targetIds = groups.Select(g => g.Target.Id).ToList();
        var chC  = await CountByCustomer(_db.ContactHistories.IgnoreQueryFilters().Where(x => targetIds.Contains(x.CustomerId) && x.CreatedAt < cutoff).Select(x => x.CustomerId), ct);
        var tkC  = await CountByCustomer(_db.CustomerTasks.IgnoreQueryFilters().Where(x => targetIds.Contains(x.CustomerId) && x.CreatedAt < cutoff).Select(x => x.CustomerId), ct);
        var opC  = await CountByCustomer(_db.Opportunities.IgnoreQueryFilters().Where(x => targetIds.Contains(x.CustomerId) && x.CreatedAt < cutoff).Select(x => x.CustomerId), ct);
        var invC = await CountByCustomer(_db.Invoices.IgnoreQueryFilters().Where(x => targetIds.Contains(x.CustomerId) && x.CreatedAt < cutoff).Select(x => x.CustomerId), ct);
        var conC = await CountByCustomer(_db.CustomerContracts.IgnoreQueryFilters().Where(x => targetIds.Contains(x.CustomerId) && x.CreatedAt < cutoff).Select(x => x.CustomerId), ct);

        foreach (var g in groups)
        {
            g.Ch = chC.GetValueOrDefault(g.Target.Id);
            g.Tk = tkC.GetValueOrDefault(g.Target.Id);
            g.Op = opC.GetValueOrDefault(g.Target.Id);
            g.Inv = invC.GetValueOrDefault(g.Target.Id);
            g.Con = conC.GetValueOrDefault(g.Target.Id);
        }

        // ── Zombie discovery: live rows that reuse a restored (ProjectId, LegacyId) ──
        // The EMS sync re-inserted retired companies while the sources sat under the
        // EMSMIGRATED marker. Childless ones are deleted on execute; ones with children
        // are only reported.
        var zombieIds = new List<Guid>();
        foreach (var g in groups)
        {
            foreach (var (db, planSrc) in g.RestoreMap)
            {
                if (!byLegacy.TryGetValue(planSrc.OriginalLegacyId, out var clashes)) continue;
                foreach (var z in clashes.Where(z => z.ProjectId == db.ProjectId && !z.IsDeleted))
                    g.Zombies.Add(z);
            }
            zombieIds.AddRange(g.Zombies.Select(z => z.Id));
        }

        var zombiesWithChildren = new HashSet<Guid>();
        if (zombieIds.Count > 0)
        {
            foreach (var q in new IQueryable<Guid>[]
            {
                _db.ContactHistories.IgnoreQueryFilters().Where(x => zombieIds.Contains(x.CustomerId)).Select(x => x.CustomerId),
                _db.CustomerTasks.IgnoreQueryFilters().Where(x => zombieIds.Contains(x.CustomerId)).Select(x => x.CustomerId),
                _db.Opportunities.IgnoreQueryFilters().Where(x => zombieIds.Contains(x.CustomerId)).Select(x => x.CustomerId),
                _db.Invoices.IgnoreQueryFilters().Where(x => zombieIds.Contains(x.CustomerId)).Select(x => x.CustomerId),
                _db.CustomerContracts.IgnoreQueryFilters().Where(x => zombieIds.Contains(x.CustomerId)).Select(x => x.CustomerId),
            })
            {
                foreach (var id in await q.Distinct().ToListAsync(ct))
                    zombiesWithChildren.Add(id);
            }
        }

        // ── Execute ──────────────────────────────────────────────────────────
        if (!dryRun && groups.Count > 0)
            await ExecuteAsync(groups, zombiesWithChildren, cutoff, warnings);

        // ── Report ───────────────────────────────────────────────────────────
        var results = new List<RollbackGroupResult>(skipped);
        int sourcesRestored = 0, childrenBack = 0, undeleted = 0, zombiesDeleted = 0;

        foreach (var g in groups)
        {
            var deletableZombies = g.Zombies.Count(z => !zombiesWithChildren.Contains(z.Id));
            var zombieKeepers = g.Zombies.Count - deletableZombies;
            if (zombieKeepers > 0)
                warnings.Add($"{g.Pair.TargetLegacyId} grubunda {zombieKeepers} zombi satır ÇOCUKLU olduğu için silinmedi — elle inceleyin.");

            sourcesRestored += g.RestoreMap.Count;
            childrenBack += g.Ch + g.Tk + g.Op + g.Inv + g.Con;
            if (g.Pair.TargetWasSoftDeleted) undeleted++;
            zombiesDeleted += deletableZombies;

            results.Add(new RollbackGroupResult(
                TargetLegacyId:            g.Pair.TargetLegacyId,
                TargetCompanyName:         g.Target.CompanyName,
                RestoredCanonicalLegacyId: g.RestoreMap.First(m => ReferenceEquals(m.Db, g.Canonical)).Plan.OriginalLegacyId,
                SourcesRestored:           g.RestoreMap.Count,
                ContactHistories:          g.Ch,
                Tasks:                     g.Tk,
                Opportunities:             g.Op,
                Invoices:                  g.Inv,
                Contracts:                 g.Con,
                ClearedFields:             g.Pair.CopiedFields,
                TargetUndeleted:           g.Pair.TargetWasSoftDeleted,
                ZombiesDeleted:            deletableZombies,
                Skipped:                   null));
        }

        var report = new EmsMigrationRollbackReport(
            DryRun:            dryRun,
            GroupsProcessed:   groups.Count,
            GroupsSkipped:     skipped.Count,
            SourcesRestored:   sourcesRestored,
            ChildrenMovedBack: childrenBack,
            TargetsUndeleted:  undeleted,
            ZombiesDeleted:    zombiesDeleted,
            Groups:            results,
            Warnings:          warnings);

        _logger.Log(
            dryRun ? LogLevel.Information : LogLevel.Warning,
            "EMS migration ROLLBACK {Mode}: groups={Groups} skipped={Skipped} sources={Sources} " +
            "childrenBack={Children} undeleted={Undeleted} zombiesDeleted={Zombies}.",
            dryRun ? "DRY-RUN" : "EXECUTED",
            groups.Count, skipped.Count, sourcesRestored, childrenBack, undeleted, zombiesDeleted);

        return report;
    }

    // ── Execute phase ────────────────────────────────────────────────────────

    private async Task ExecuteAsync(
        List<GroupWork> groups, HashSet<Guid> zombiesWithChildren, DateTime cutoff, List<string> warnings)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        var lockTaken = false;
        try
        {
            lockTaken = await ExecuteScalarBoolAsync(
                $"SELECT pg_try_advisory_lock({SyncTimerService.AdvisoryLockKey})");
            if (!lockTaken)
                throw new InvalidOperationException(
                    "Sync kilidi alınamadı — senkronizasyon şu anda çalışıyor. Lütfen kısa süre sonra tekrar deneyin.");

            var now = DateTime.UtcNow;

            foreach (var g in groups)
            {
                // 5a. Delete childless zombies FIRST so the restored LegacyIds don't collide
                // with live duplicates.
                foreach (var z in g.Zombies.Where(z => !zombiesWithChildren.Contains(z.Id)))
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        @"DELETE FROM ""Customers"" WHERE ""Id"" = {0}", z.Id);
                    // Also drop from the change tracker so SaveChanges doesn't try to update it.
                    _db.Entry(z).State = EntityState.Detached;
                }

                // 1. Move children back: everything on the target created before the cutoff.
                g.Ch  = await MoveChildren("ContactHistories",  g, cutoff, now);
                g.Tk  = await MoveChildren("CustomerTasks",     g, cutoff, now);
                g.Op  = await MoveChildren("Opportunities",     g, cutoff, now);
                g.Inv = await MoveChildren("Invoices",          g, cutoff, now);
                g.Con = await MoveChildren("CustomerContracts", g, cutoff, now);

                // 2. Clear the copied CRM-only fields on the target (they were empty pre-migration).
                foreach (var field in g.Pair.CopiedFields)
                    ClearField(g.Target, field, warnings);

                // 3. Undo the carry-over soft-delete.
                if (g.Pair.TargetWasSoftDeleted)
                    g.Target.IsDeleted = false;

                // 4. Restore sources: original LegacyId + original deletion flag.
                foreach (var (db, planSrc) in g.RestoreMap)
                {
                    db.LegacyId = planSrc.OriginalLegacyId;
                    db.IsDeleted = planSrc.WasDeleted;
                }
            }

            await _db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
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

    private async Task<int> MoveChildren(string table, GroupWork g, DateTime cutoff, DateTime now)
    {
        // Fixed table names (whitelist below); parameters are positional.
        var sql = table switch
        {
            "ContactHistories"  => @"UPDATE ""ContactHistories""  SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3} AND ""CreatedAt"" < {4}",
            "CustomerTasks"     => @"UPDATE ""CustomerTasks""     SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3} AND ""CreatedAt"" < {4}",
            "Opportunities"     => @"UPDATE ""Opportunities""     SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3} AND ""CreatedAt"" < {4}",
            "Invoices"          => @"UPDATE ""Invoices""          SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3} AND ""CreatedAt"" < {4}",
            "CustomerContracts" => @"UPDATE ""CustomerContracts"" SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3} AND ""CreatedAt"" < {4}",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return await _db.Database.ExecuteSqlRawAsync(
            sql, g.Canonical.Id, g.Canonical.ProjectId, now, g.Target.Id, cutoff);
    }

    private static void ClearField(Customer target, string field, List<string> warnings)
    {
        switch (field)
        {
            case "Code":              target.Code = null; break;
            case "ContactName":       target.ContactName = null; break;
            case "Label":             target.Label = null; break;
            case "AssignedUserId":    target.AssignedUserId = null; break;
            case "ParasutContactId":  target.ParasutContactId = null; break;
            case "IsEInvoicePayer":   target.IsEInvoicePayer = false; break;
            case "EInvoiceAddress":   target.EInvoiceAddress = null; break;
            case "MonthlyLicenseFee": target.MonthlyLicenseFee = null; break;
            default:
                warnings.Add($"Bilinmeyen kopyalanmış alan '{field}' — temizlenmedi.");
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class GroupWork
    {
        public GroupWork(
            RollbackPairPlan pair, int numericId, Customer target,
            List<(Customer Db, RollbackSourcePlan Plan)> restoreMap, Customer canonical)
        {
            Pair = pair; NumericId = numericId; Target = target;
            RestoreMap = restoreMap; Canonical = canonical;
        }

        public RollbackPairPlan Pair { get; }
        public int NumericId { get; }
        public Customer Target { get; }
        public List<(Customer Db, RollbackSourcePlan Plan)> RestoreMap { get; }
        public Customer Canonical { get; }
        public List<Customer> Zombies { get; } = new();
        public int Ch; public int Tk; public int Op; public int Inv; public int Con;
    }

    /// <summary>
    /// Pairs the DB's EMSMIGRATED rows with the plan's original-source entries: first by
    /// normalized company name, then leftovers in order. Counts must match or a warning is added
    /// (extra DB rows restore with the first unused plan entry; extra plan entries are ignored).
    /// </summary>
    private static List<(Customer Db, RollbackSourcePlan Plan)> MatchSources(
        List<Customer> dbRows, List<RollbackSourcePlan> planned, List<string> warnings, string marker)
    {
        var result = new List<(Customer, RollbackSourcePlan)>();
        var remainingPlans = new List<RollbackSourcePlan>(planned);

        foreach (var db in dbRows)
        {
            var match = remainingPlans.FirstOrDefault(p =>
                Normalize(p.CompanyName) == Normalize(db.CompanyName));
            if (match is null && remainingPlans.Count > 0) match = remainingPlans[0];
            if (match is null)
            {
                warnings.Add($"{marker}: plan girdisi kalmadı — satır numeric id ile geri yüklendi.");
                match = new RollbackSourcePlan(
                    marker.Replace("EMSMIGRATED-", ""), db.CompanyName, db.IsDeleted);
            }
            remainingPlans.Remove(match);
            result.Add((db, match));
        }

        return result;
    }

    private static string Normalize(string? s) =>
        new string((s ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static RollbackGroupResult Skip(RollbackPairPlan pair, string? targetName, string reason) =>
        new(pair.TargetLegacyId, targetName, null, 0, 0, 0, 0, 0, 0,
            new List<string>(), false, 0, reason);

    private static bool TryNumericId(string legacyId, string prefix, out int id)
    {
        id = 0;
        return legacyId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(legacyId[prefix.Length..], out id);
    }

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
