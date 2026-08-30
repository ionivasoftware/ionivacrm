using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>One zombie row found (and, on execute, deleted or kept).</summary>
public sealed record ZombieRow(
    string LegacyId,
    string CompanyName,
    string Status,
    bool HasChildren);

/// <summary>Full report returned by <see cref="EmsZombiePurgeService.PurgeAsync"/>.</summary>
public sealed record ZombiePurgeReport(
    bool DryRun,
    int ZombiesFound,
    int ZombiesDeleted,
    int KeptWithChildren,
    List<ZombieRow> Zombies,
    List<string> Warnings);

/// <summary>
/// Deletes ZOMBIE customer rows the (now-disabled) EMS sync re-inserted after the migration.
///
/// When the EMS→Liftdesk migration retired a source row as <c>EMSMIGRATED-{n}</c>, the next EMS
/// sync cycle could no longer find company id {n} and re-inserted it as a fresh, childless row
/// with the bare-numeric LegacyId "{n}". A ZOMBIE is therefore precisely: a LIVE row whose
/// LegacyId is bare numeric {n} AND whose project contains an <c>EMSMIGRATED-{n}</c> row (proof
/// the real company was migrated). Rows without a matching marker are genuine un-migrated
/// customers (unmatched/ambiguous names) and are never touched.
///
/// Zombies with ANY child row (contact histories, tasks, opportunities, invoices, contracts)
/// are kept and reported instead of deleted — data never disappears silently.
///
/// EXECUTE MODE mirrors the other one-shot services: sync advisory lock BEFORE the snapshot
/// read, one transaction, <see cref="CancellationToken.None"/> past that point. DRY-RUN is pure.
/// </summary>
public sealed class EmsZombiePurgeService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmsZombiePurgeService> _logger;

    /// <summary>Initialises a new instance of <see cref="EmsZombiePurgeService"/>.</summary>
    public EmsZombiePurgeService(ApplicationDbContext db, ILogger<EmsZombiePurgeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Runs the purge (or a read-only preview when <paramref name="dryRun"/> is true).</summary>
    public async Task<ZombiePurgeReport> PurgeAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
            return await RunAsync(dryRun: true, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        var lockTaken = false;
        try
        {
            lockTaken = await ExecuteScalarBoolAsync(
                $"SELECT pg_try_advisory_lock({SyncTimerService.AdvisoryLockKey})");
            if (!lockTaken)
                throw new InvalidOperationException(
                    "Sync kilidi alınamadı — senkronizasyon şu anda çalışıyor. Lütfen kısa süre sonra tekrar deneyin.");

            var report = await RunAsync(dryRun: false, CancellationToken.None);
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

    private async Task<ZombiePurgeReport> RunAsync(bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();

        var allCustomers = await _db.Customers
            .IgnoreQueryFilters()
            .Select(c => new { c.Id, c.ProjectId, c.LegacyId, c.CompanyName, c.Status, c.IsDeleted })
            .ToListAsync(ct);

        // (ProjectId, numeric id) pairs proven migrated — the EMSMIGRATED markers.
        var migrated = new HashSet<(Guid, int)>();
        foreach (var c in allCustomers)
        {
            if (c.LegacyId is not null
                && c.LegacyId.StartsWith("EMSMIGRATED-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(c.LegacyId["EMSMIGRATED-".Length..], out var n))
                migrated.Add((c.ProjectId, n));
        }

        // Zombies: LIVE bare-numeric rows whose (project, id) has a marker.
        var zombies = allCustomers
            .Where(c => !c.IsDeleted
                        && c.LegacyId is not null
                        && int.TryParse(c.LegacyId, out var n)
                        && migrated.Contains((c.ProjectId, n)))
            .ToList();

        var zombieIds = zombies.Select(z => z.Id).ToList();
        var withChildren = new HashSet<Guid>();
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
                    withChildren.Add(id);
            }
        }

        var deletable = zombies.Where(z => !withChildren.Contains(z.Id)).ToList();
        var keepers = zombies.Count - deletable.Count;
        if (keepers > 0)
            warnings.Add($"{keepers} zombi satır ÇOCUKLU olduğu için silinmedi — elle inceleyin.");

        if (!dryRun)
        {
            foreach (var z in deletable)
                await _db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Customers"" WHERE ""Id"" = {0}", z.Id);
        }

        var report = new ZombiePurgeReport(
            DryRun:           dryRun,
            ZombiesFound:     zombies.Count,
            ZombiesDeleted:   deletable.Count,
            KeptWithChildren: keepers,
            Zombies: zombies
                .OrderBy(z => z.CompanyName, StringComparer.OrdinalIgnoreCase)
                .Select(z => new ZombieRow(z.LegacyId!, z.CompanyName, z.Status.ToString(), withChildren.Contains(z.Id)))
                .ToList(),
            Warnings: warnings);

        _logger.Log(
            dryRun ? LogLevel.Information : LogLevel.Warning,
            "EMS zombie purge {Mode}: found={Found} deleted={Deleted} keptWithChildren={Kept}.",
            dryRun ? "DRY-RUN" : "EXECUTED", zombies.Count, deletable.Count, keepers);

        return report;
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
}
