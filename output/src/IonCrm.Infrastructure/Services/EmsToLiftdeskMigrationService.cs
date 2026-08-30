using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>One EMS source row and what moves off it onto its Liftdesk target.</summary>
public sealed record EmsLiftdeskPair(
    string EmsLegacyId,
    string EmsCompanyName,
    bool EmsWasDeleted,
    string TargetLegacyId,
    string TargetCompanyName,
    bool TargetWasDeleted,
    int ContactHistories,
    int Tasks,
    int Opportunities,
    int Invoices,
    int Contracts,
    List<string> CopiedFields,
    bool TargetSoftDeleted);

/// <summary>An EMS customer that was not migrated (left untouched), with the reason.</summary>
public sealed record EmsLiftdeskUnmatched(
    string LegacyId,
    string CompanyName,
    bool IsDeleted,
    string Reason);

/// <summary>Full report returned by <see cref="EmsToLiftdeskMigrationService.MigrateAsync"/>.</summary>
public sealed record EmsToLiftdeskMigrationReport(
    bool DryRun,
    int EmsCustomersFound,
    int MigratedPairs,
    int UnmatchedCount,
    int ContactHistoriesMoved,
    int TasksMoved,
    int OpportunitiesMoved,
    int InvoicesMoved,
    int ContractsMoved,
    int TargetsSoftDeleted,
    List<EmsLiftdeskPair> Pairs,
    List<EmsLiftdeskUnmatched> Unmatched,
    List<string> Warnings);

/// <summary>
/// One-shot migration: EMS (the retired SaaS) customers → their Liftdesk successors.
///
/// The EMS platform was shut down and its tenants were migrated to Liftdesk with COMPANY IDS
/// PRESERVED, so an EMS customer with LegacyId <c>"3"</c> / <c>"SAASA-3"</c> / <c>"EMS-3"</c>
/// corresponds to the Liftdesk-synced customer with LegacyId <c>"LIFT-3"</c>.
///
/// Processing is TARGET-GROUP based: all EMS rows that resolve to the same LIFT-{id} customer
/// (e.g. a leftover "SAASA-3" duplicate beside the canonical "3") are handled as one group with
/// one CANONICAL source (a live row when available). For each group:
///   1. Every source's child rows (ContactHistories, CustomerTasks, Opportunities, Invoices,
///      CustomerContracts) are re-pointed to the Liftdesk customer, rewriting the denormalized
///      <c>ProjectId</c> to the target's project so tenant filters keep working across projects.
///      Soft-deleted children move too — the EMS rows are fully retired, their archive belongs
///      with the successor.
///   2. CRM-only fields the syncs never write (Code, ContactName, Label, AssignedUserId,
///      ParasutContactId, IsEInvoicePayer, EInvoiceAddress, MonthlyLicenseFee) are copied from
///      the CANONICAL source onto the target, only where the target's own value is empty.
///      Sync-managed fields (CompanyName, Email, Phone, Segment, Status, ExpirationDate, …) are
///      NOT copied — the next Liftdesk sync would overwrite them anyway.
///   3. Deletion carry-over is decided per GROUP: the live target is soft-deleted only when
///      EVERY source in the group was user-deleted. (A deleted duplicate must never bury a live
///      sibling's archive under a deleted target.)
///   4. Each source row is retired: soft-delete + LegacyId rewritten to <c>EMSMIGRATED-{id}</c>,
///      making the whole operation idempotent while keeping the original id inside the marker.
///
/// Groups whose target was ALREADY soft-deleted while any source is live are skipped and
/// reported — moving live data under a deleted row would hide it, so that case needs a human.
/// EMS customers with no <c>LIFT-{id}</c> counterpart are reported and left untouched.
///
/// EXECUTE MODE runs inside one DB transaction and takes the SAME PostgreSQL advisory lock as
/// <see cref="SyncTimerService"/>: the 15-minute sync cannot run mid-migration (its reconcile
/// could otherwise soft-delete a target we are moving data onto), and a second concurrent
/// invocation of this endpoint is rejected outright. After the first write, the operation is
/// past the point of no return — commit/rollback run with <see cref="CancellationToken.None"/>
/// so a client timeout cannot leave a committed transaction reported as rolled back.
/// DRY-RUN performs zero writes, takes no lock, and returns the identical report shape.
/// </summary>
public sealed class EmsToLiftdeskMigrationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmsToLiftdeskMigrationService> _logger;

    /// <summary>Initialises a new instance of <see cref="EmsToLiftdeskMigrationService"/>.</summary>
    public EmsToLiftdeskMigrationService(
        ApplicationDbContext db,
        ILogger<EmsToLiftdeskMigrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Internal planning shapes ─────────────────────────────────────────────

    private sealed class SourcePlan
    {
        public required Customer Ems { get; init; }
        public required int NumericId { get; init; }
        /// <summary>Pre-mutation snapshot — the report must show the ORIGINAL state.</summary>
        public required bool EmsWasDeleted { get; init; }
        public int Ch; public int Tk; public int Op; public int Inv; public int Con;
        public List<string> Copied { get; } = new();
    }

    private sealed class TargetGroup
    {
        public required Customer Target { get; init; }
        /// <summary>Pre-mutation snapshot of the target's IsDeleted flag.</summary>
        public required bool TargetOriginalDeleted { get; init; }
        public List<SourcePlan> Sources { get; } = new();
        public SourcePlan Canonical => Sources.FirstOrDefault(s => !s.EmsWasDeleted) ?? Sources[0];
        /// <summary>Soft-delete the target only when EVERY source was user-deleted.</summary>
        public bool CarryOverDelete => !TargetOriginalDeleted && Sources.All(s => s.EmsWasDeleted);
    }

    /// <summary>Runs the migration (or a read-only preview when <paramref name="dryRun"/> is true).</summary>
    public async Task<EmsToLiftdeskMigrationReport> MigrateAsync(bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();

        // Tracked load on purpose: field copies + retire flags below go through EF change
        // tracking so the SaveChanges interceptor stamps UpdatedAt consistently.
        // IgnoreQueryFilters — must see soft-deleted rows and every project.
        var allCustomers = await _db.Customers
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        // ── Liftdesk index: numeric id → customer (prefer the live row on duplicates) ──
        var liftById = new Dictionary<int, Customer>();
        foreach (var c in allCustomers)
        {
            if (c.LegacyId is null || !c.LegacyId.StartsWith("LIFT-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(c.LegacyId["LIFT-".Length..], out var id))
            {
                warnings.Add($"LIFT satırı sayısal olmayan id taşıyor, atlandı: '{c.LegacyId}' ({c.CompanyName})");
                continue;
            }
            if (liftById.TryGetValue(id, out var existing))
            {
                if (existing.IsDeleted && !c.IsDeleted)
                    liftById[id] = c;
                warnings.Add($"Birden fazla LIFT-{id} satırı bulundu — canlı olan tercih edildi.");
                continue;
            }
            liftById[id] = c;
        }

        // ── Classify EMS candidates ──────────────────────────────────────────
        var unmatched = new List<EmsLiftdeskUnmatched>();
        var candidates = new List<(Customer Customer, int NumericId)>();
        int parseFailedCount = 0;

        foreach (var c in allCustomers)
        {
            if (!TryClassifyEms(c.LegacyId, out var numericId, out var parseFailed))
            {
                if (parseFailed)
                {
                    parseFailedCount++;
                    unmatched.Add(new EmsLiftdeskUnmatched(
                        c.LegacyId ?? "(null)", c.CompanyName, c.IsDeleted,
                        "EMS deseninde ama sayısal id çözümlenemedi"));
                }
                continue;
            }
            candidates.Add((c, numericId));
        }

        // ── Build target groups ──────────────────────────────────────────────
        var groupsByTarget = new Dictionary<Guid, TargetGroup>();
        foreach (var (ems, numericId) in candidates)
        {
            if (!liftById.TryGetValue(numericId, out var target))
            {
                unmatched.Add(new EmsLiftdeskUnmatched(
                    ems.LegacyId!, ems.CompanyName, ems.IsDeleted,
                    $"LIFT-{numericId} karşılığı bulunamadı"));
                continue;
            }

            if (!groupsByTarget.TryGetValue(target.Id, out var group))
            {
                group = new TargetGroup { Target = target, TargetOriginalDeleted = target.IsDeleted };
                groupsByTarget[target.Id] = group;
            }
            group.Sources.Add(new SourcePlan { Ems = ems, NumericId = numericId, EmsWasDeleted = ems.IsDeleted });
        }

        // Group-level human-decision guard: an ALREADY-deleted target with any live source
        // would hide live data — skip the whole group and surface every source.
        var groups = new List<TargetGroup>();
        foreach (var group in groupsByTarget.Values)
        {
            if (group.TargetOriginalDeleted && group.Sources.Any(s => !s.EmsWasDeleted))
            {
                foreach (var s in group.Sources)
                    unmatched.Add(new EmsLiftdeskUnmatched(
                        s.Ems.LegacyId!, s.Ems.CompanyName, s.Ems.IsDeleted,
                        $"Hedef {group.Target.LegacyId} ({group.Target.CompanyName}) silinmiş durumda ve grupta canlı EMS satırı var. Manuel karar gerekli."));
                continue;
            }
            if (group.Sources.Count > 1)
                warnings.Add(
                    $"{group.Sources.Count} EMS satırı aynı hedefe eşlendi ({group.Target.LegacyId}): " +
                    string.Join(", ", group.Sources.Select(s => s.Ems.LegacyId)) +
                    " — alan kopyalama yalnızca kanonik satırdan yapılır.");
            groups.Add(group);
        }

        var allSources = groups.SelectMany(g => g.Sources).ToList();

        // ── Child counts (authoritative for dry-run; execute overwrites with real rowcounts) ──
        var emsIds = allSources.Select(s => s.Ems.Id).ToList();
        var chCounts   = await CountByCustomerAsync(_db.ContactHistories.IgnoreQueryFilters().Where(x => emsIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var taskCounts = await CountByCustomerAsync(_db.CustomerTasks.IgnoreQueryFilters().Where(x => emsIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var oppCounts  = await CountByCustomerAsync(_db.Opportunities.IgnoreQueryFilters().Where(x => emsIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var invCounts  = await CountByCustomerAsync(_db.Invoices.IgnoreQueryFilters().Where(x => emsIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);
        var conCounts  = await CountByCustomerAsync(_db.CustomerContracts.IgnoreQueryFilters().Where(x => emsIds.Contains(x.CustomerId)).Select(x => x.CustomerId), ct);

        foreach (var s in allSources)
        {
            s.Ch  = chCounts.GetValueOrDefault(s.Ems.Id);
            s.Tk  = taskCounts.GetValueOrDefault(s.Ems.Id);
            s.Op  = oppCounts.GetValueOrDefault(s.Ems.Id);
            s.Inv = invCounts.GetValueOrDefault(s.Ems.Id);
            s.Con = conCounts.GetValueOrDefault(s.Ems.Id);
        }

        // ── Field-copy planning (from CANONICAL source, against the target's ORIGINAL values) ──
        foreach (var group in groups)
            PlanFieldCopies(group, applyChanges: false);

        // ── Operational warnings — computed BEFORE the transaction so a read failure here can
        //    never make a committed migration look failed. ────────────────────
        var emsKeyProjects = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.EmsApiKey != null && p.EmsApiKey != "")
            .Select(p => p.Name)
            .ToListAsync(ct);
        if (emsKeyProjects.Count > 0)
            warnings.Add(
                "EMS API anahtarı hâlâ yapılandırılmış projeler var: " + string.Join(", ", emsKeyProjects) +
                ". EMS kapatıldığı için her 15 dk'lık sync döngüsü başarısız log üretecek — proje ayarlarından EmsApiKey'i temizlemeniz önerilir.");

        var crossProject = groups.Sum(g => g.Sources.Count(s => s.Ems.ProjectId != g.Target.ProjectId));
        if (crossProject > 0)
            warnings.Add(
                $"{crossProject} EMS kaydı farklı projedeki hedefe taşınıyor. Taşınan taslak faturalar " +
                "hedef projenin Paraşüt bağlantısı üzerinden aktarılır (global Paraşüt bağlantısı kullanılıyorsa fark yaratmaz).");

        if (groups.Count > 0)
            warnings.Add(
                "Migration sonrası EMS arşivi LIFT müşterilerinin altında yaşar. /sync/reset-liftdesk yalnızca " +
                "çocuk kaydı olmayan LIFT satırlarını siler; yine de resetten önce bu raporu saklayın.");

        // ── Execute ──────────────────────────────────────────────────────────
        if (!dryRun && groups.Count > 0)
            await ExecuteAsync(groups);

        // ── Report (all figures from pre-mutation snapshots + real rowcounts on execute) ──
        var reportPairs = new List<EmsLiftdeskPair>();
        int chTotal = 0, taskTotal = 0, oppTotal = 0, invTotal = 0, conTotal = 0;
        int targetsSoftDeleted = 0;

        foreach (var group in groups)
        {
            if (group.CarryOverDelete) targetsSoftDeleted++;
            foreach (var s in group.Sources)
            {
                chTotal += s.Ch; taskTotal += s.Tk; oppTotal += s.Op; invTotal += s.Inv; conTotal += s.Con;
                reportPairs.Add(new EmsLiftdeskPair(
                    EmsLegacyId:       dryRun ? s.Ems.LegacyId! : $"EMSMIGRATED-{s.NumericId}",
                    EmsCompanyName:    s.Ems.CompanyName,
                    EmsWasDeleted:     s.EmsWasDeleted,
                    TargetLegacyId:    group.Target.LegacyId!,
                    TargetCompanyName: group.Target.CompanyName,
                    TargetWasDeleted:  group.TargetOriginalDeleted,
                    ContactHistories:  s.Ch,
                    Tasks:             s.Tk,
                    Opportunities:     s.Op,
                    Invoices:          s.Inv,
                    Contracts:         s.Con,
                    CopiedFields:      s.Copied,
                    TargetSoftDeleted: group.CarryOverDelete && ReferenceEquals(s, group.Canonical)));
            }
        }

        // Found = every row that LOOKED like an EMS key (parse failures included) so that
        // Found == MigratedPairs + UnmatchedCount always reconciles for the operator.
        var report = new EmsToLiftdeskMigrationReport(
            DryRun:                dryRun,
            EmsCustomersFound:     candidates.Count + parseFailedCount,
            MigratedPairs:         allSources.Count,
            UnmatchedCount:        unmatched.Count,
            ContactHistoriesMoved: chTotal,
            TasksMoved:            taskTotal,
            OpportunitiesMoved:    oppTotal,
            InvoicesMoved:         invTotal,
            ContractsMoved:        conTotal,
            TargetsSoftDeleted:    targetsSoftDeleted,
            Pairs:                 reportPairs,
            Unmatched:             unmatched,
            Warnings:              warnings);

        _logger.Log(
            dryRun ? LogLevel.Information : LogLevel.Warning,
            "EMS→Liftdesk migration {Mode}: sources={Sources} groups={Groups} unmatched={Unmatched} " +
            "ch={Ch} tasks={Tasks} opps={Opps} invoices={Inv} contracts={Con} targetsSoftDeleted={Del}.",
            dryRun ? "DRY-RUN" : "EXECUTED",
            allSources.Count, groups.Count, unmatched.Count,
            chTotal, taskTotal, oppTotal, invTotal, conTotal, targetsSoftDeleted);

        return report;
    }

    // ── Execute phase ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the plan inside one transaction. Uses <see cref="CancellationToken.None"/> for every
    /// DB call: past the first write the operation must either fully commit or fully roll back —
    /// honoring a client-abort token mid-commit can leave a durably-committed transaction that the
    /// caller is told was rolled back.
    /// </summary>
    private async Task ExecuteAsync(List<TargetGroup> groups)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        var lockTaken = false;
        try
        {
            // Same advisory lock as SyncTimerService: blocks the 15-minute sync (whose Liftdesk
            // reconcile could soft-delete a target mid-migration) and rejects a concurrent second
            // invocation of this endpoint. Non-blocking: fail fast instead of queueing.
            lockTaken = await ExecuteScalarBoolAsync(
                $"SELECT pg_try_advisory_lock({SyncTimerService.AdvisoryLockKey})");
            if (!lockTaken)
                throw new InvalidOperationException(
                    "Sync kilidi alınamadı — 15 dakikalık senkronizasyon (veya başka bir migration çağrısı) şu anda çalışıyor. Lütfen kısa süre sonra tekrar deneyin.");

            var now = DateTime.UtcNow;

            foreach (var group in groups)
            {
                foreach (var s in group.Sources)
                {
                    // Move ALL children (soft-deleted ones included) and rewrite the denormalized
                    // ProjectId. Capture REAL affected-row counts so the execute report reflects
                    // what actually moved, not a pre-transaction estimate.
                    s.Ch  = await _db.Database.ExecuteSqlRawAsync(ChildMoveStatements[0], group.Target.Id, group.Target.ProjectId, now, s.Ems.Id);
                    s.Tk  = await _db.Database.ExecuteSqlRawAsync(ChildMoveStatements[1], group.Target.Id, group.Target.ProjectId, now, s.Ems.Id);
                    s.Op  = await _db.Database.ExecuteSqlRawAsync(ChildMoveStatements[2], group.Target.Id, group.Target.ProjectId, now, s.Ems.Id);
                    s.Inv = await _db.Database.ExecuteSqlRawAsync(ChildMoveStatements[3], group.Target.Id, group.Target.ProjectId, now, s.Ems.Id);
                    s.Con = await _db.Database.ExecuteSqlRawAsync(ChildMoveStatements[4], group.Target.Id, group.Target.ProjectId, now, s.Ems.Id);

                    // Retire the source. EMSMIGRATED- keeps re-runs idempotent and preserves the
                    // original numeric id for audit.
                    s.Ems.IsDeleted = true;
                    s.Ems.LegacyId = $"EMSMIGRATED-{s.NumericId}";
                }

                // Field copies from the canonical source (plan already computed the list).
                PlanFieldCopies(group, applyChanges: true);

                // Deletion carry-over: only when every source was user-deleted.
                if (group.CarryOverDelete)
                    group.Target.IsDeleted = true;
            }

            await _db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        catch
        {
            // Guarded rollback: if the transaction already completed (e.g. commit raced a broken
            // connection), RollbackAsync would throw and MASK the original exception.
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { /* transaction already completed or connection gone — original exception wins */ }
            throw;
        }
        finally
        {
            if (lockTaken)
            {
                // Best-effort session-lock release; the pooled connection would otherwise keep it.
                try { await ExecuteScalarBoolAsync($"SELECT pg_advisory_unlock({SyncTimerService.AdvisoryLockKey})"); }
                catch { /* connection may be gone; lock dies with the session */ }
            }
        }
    }

    /// <summary>
    /// Computes (and in execute mode applies) the CRM-only field copies for a group: canonical
    /// source → target, only where the target's ORIGINAL value is empty. Dry-run and execute call
    /// the same logic so the preview cannot diverge from what execute does. On the second call
    /// (execute) the list is rebuilt from the same original target values because the plan pass
    /// never mutated them.
    /// </summary>
    private static void PlanFieldCopies(TargetGroup group, bool applyChanges)
    {
        var target = group.Target;
        var ems = group.Canonical.Ems;
        var copied = group.Canonical.Copied;
        if (applyChanges == false) copied.Clear(); // plan pass owns the list; execute re-verifies

        void CopyIf(string name, bool condition, Action apply)
        {
            if (!condition) return;
            if (applyChanges) apply();
            else copied.Add(name);
        }

        CopyIf("Code",             target.Code is null && ems.Code is not null,
            () => target.Code = ems.Code);
        CopyIf("ContactName",      string.IsNullOrEmpty(target.ContactName) && !string.IsNullOrEmpty(ems.ContactName),
            () => target.ContactName = ems.ContactName);
        CopyIf("Label",            target.Label is null && ems.Label is not null,
            () => target.Label = ems.Label);
        CopyIf("AssignedUserId",   target.AssignedUserId is null && ems.AssignedUserId is not null,
            () => target.AssignedUserId = ems.AssignedUserId);
        CopyIf("ParasutContactId", string.IsNullOrEmpty(target.ParasutContactId) && !string.IsNullOrEmpty(ems.ParasutContactId),
            () => target.ParasutContactId = ems.ParasutContactId);
        CopyIf("IsEInvoicePayer",  !target.IsEInvoicePayer && ems.IsEInvoicePayer,
            () => target.IsEInvoicePayer = true);
        CopyIf("EInvoiceAddress",  string.IsNullOrEmpty(target.EInvoiceAddress) && !string.IsNullOrEmpty(ems.EInvoiceAddress),
            () => target.EInvoiceAddress = ems.EInvoiceAddress);
        CopyIf("MonthlyLicenseFee", target.MonthlyLicenseFee is null && ems.MonthlyLicenseFee is not null,
            () => target.MonthlyLicenseFee = ems.MonthlyLicenseFee);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fixed re-pointing statements for the five child tables (no dynamic SQL — table names are
    /// baked in; the {0}-{3} placeholders are parameterized).
    /// Parameters: {0} target CustomerId, {1} target ProjectId, {2} UpdatedAt, {3} source CustomerId.
    /// </summary>
    private static readonly string[] ChildMoveStatements =
    {
        @"UPDATE ""ContactHistories""  SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3}",
        @"UPDATE ""CustomerTasks""     SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3}",
        @"UPDATE ""Opportunities""     SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3}",
        @"UPDATE ""Invoices""          SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3}",
        @"UPDATE ""CustomerContracts"" SET ""CustomerId"" = {0}, ""ProjectId"" = {1}, ""UpdatedAt"" = {2} WHERE ""CustomerId"" = {3}",
    };

    /// <summary>
    /// Classifies a LegacyId as an EMS customer key and extracts its numeric company id.
    /// Accepted: bare numeric ("3"), "SAASA-3" (legacy sync prefix), "EMS-3" (earliest
    /// DataMigrationService runs). Explicitly excluded: EMSMIGRATED- (already processed —
    /// checked BEFORE the EMS- branch), LIFT- / REZV- / SAASB- (other sources), PC- (CRM-local
    /// leads). <paramref name="parseFailed"/> is true when the value LOOKED like an EMS key but
    /// its numeric part would not parse — surfaced in the report instead of silently skipped.
    /// </summary>
    private static bool TryClassifyEms(string? legacyId, out int numericId, out bool parseFailed)
    {
        numericId = 0;
        parseFailed = false;
        if (string.IsNullOrEmpty(legacyId)) return false;

        if (legacyId.StartsWith("EMSMIGRATED-", StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("LIFT-",  StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("REZV-",  StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("PC-",    StringComparison.OrdinalIgnoreCase)) return false;

        string raw;
        if (legacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase))
            raw = legacyId["SAASA-".Length..];
        else if (legacyId.StartsWith("EMS-", StringComparison.OrdinalIgnoreCase))
            raw = legacyId["EMS-".Length..];
        else if (char.IsDigit(legacyId[0]))
            raw = legacyId;
        else
            return false; // some other non-EMS shape — not our concern

        if (int.TryParse(raw, out numericId)) return true;

        parseFailed = true;
        return false;
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

    private static async Task<Dictionary<Guid, int>> CountByCustomerAsync(
        IQueryable<Guid> customerIds, CancellationToken ct)
    {
        return await customerIds
            .GroupBy(id => id)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }
}
