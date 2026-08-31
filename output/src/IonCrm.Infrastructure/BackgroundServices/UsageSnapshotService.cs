using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Writes the monthly <see cref="CustomerUsageSnapshot"/> rows that power the churn dashboard.
///
/// Runs once a day and upserts the CURRENT month's snapshot for every Liftdesk customer from the
/// live summary + plan endpoints. Re-running the same month overwrites that month's row with the
/// latest figures (the month "settles" as it closes); a new month naturally starts a new row. The
/// unique index on (CustomerId, SnapshotYear, SnapshotMonth) is the idempotency guarantee — one
/// row per firm per month no matter how often the job fires.
///
/// This is intentionally decoupled from the 15-minute sync: it is read-heavy (one summary + one
/// plan HTTP call per customer) and only needs to run occasionally. It takes its OWN advisory lock
/// (distinct key from <see cref="SyncTimerService"/>) so a rolling-deploy double-container can't
/// double-write, but it does NOT block or get blocked by the sync.
///
/// Fields that depend on Liftdesk work not yet shipped — <see cref="CustomerUsageSnapshot.LastLoginAt"/>
/// and <see cref="CustomerUsageSnapshot.WorkOrderCount"/> — stay null/0 until the summary endpoint
/// starts returning them, at which point they populate automatically (no code change here needed
/// beyond reading the new fields once they exist on the wire DTO).
/// </summary>
public sealed class UsageSnapshotService : BackgroundService
{
    /// <summary>Distinct advisory-lock key — must NOT collide with <see cref="SyncTimerService.AdvisoryLockKey"/>.</summary>
    private const long AdvisoryLockKey = 7_391_827_364_918_291L;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Startup delay so migrations + the idempotent SQL bootstrap (which creates the table) finish first.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UsageSnapshotService> _logger;

    /// <summary>Initialises a new instance of <see cref="UsageSnapshotService"/>.</summary>
    public UsageSnapshotService(IServiceScopeFactory scopeFactory, ILogger<UsageSnapshotService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UsageSnapshotService started — capturing usage snapshots every {Hours}h.", Interval.TotalHours);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CaptureAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UsageSnapshotService capture cycle failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Captures the current-month snapshot for every Liftdesk customer.</summary>
    private async Task CaptureAllAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saasAClient = scope.ServiceProvider.GetRequiredService<ISaasAClient>();
        var planClient  = scope.ServiceProvider.GetRequiredService<ILiftdeskPlanClient>();
        var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

        // Single-writer guard across rolling-deploy containers. Non-blocking: skip this cycle if
        // another instance is already capturing.
        var lockTaken = await TryAdvisoryLockAsync(db, ct);
        if (!lockTaken)
        {
            _logger.LogInformation("UsageSnapshot: advisory lock busy — another instance is capturing. Skipping.");
            return;
        }

        try
        {
            var now   = DateTime.UtcNow;
            var year  = now.Year;
            var month = now.Month;

            // Every live Liftdesk customer (LegacyId "LIFT-{n}"). IgnoreQueryFilters — no HTTP user here.
            var customers = await db.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId != null && c.LegacyId.StartsWith("LIFT-"))
                .ToListAsync(ct);

            _logger.LogInformation("UsageSnapshot: capturing {Count} Liftdesk customers for {Year}-{Month:D2}.",
                customers.Count, year, month);

            // Projects via GetAllAsync — it IgnoreQueryFilters (ProjectRepository), which is REQUIRED
            // here: this is a no-HTTP background scope, so the tenant query filter on Project would
            // otherwise resolve every project to null (IsSuperAdmin=false, ProjectIds=[]), leaving
            // apiKey/baseUrl null and misrouting every Liftdesk call to the EMS default host.
            var projects = (await projectRepo.GetAllAsync(ct)).ToDictionary(p => p.Id);
            int captured = 0, failed = 0;

            foreach (var customer in customers)
            {
                ct.ThrowIfCancellationRequested();

                projects.TryGetValue(customer.ProjectId, out var project);

                if (!SaasCustomerResolver.TryResolve(customer, project,
                        out var companyId, out var apiKey, out var baseUrl, out var kind)
                    || kind != SaasSourceKind.Liftdesk
                    || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(baseUrl))
                {
                    continue; // not a resolvable Liftdesk customer with live credentials — skip
                }

                try
                {
                    var summary = await saasAClient.GetCompanySummaryAsync(apiKey, companyId, ct, baseUrl);

                    // Plan is best-effort context — a plan failure must not drop the usage snapshot.
                    Application.Common.Models.ExternalApis.LiftdeskCompanyPlan? plan = null;
                    try { plan = await planClient.GetPlanAsync(baseUrl, apiKey, companyId, ct); }
                    catch (Exception ex) { _logger.LogDebug(ex, "UsageSnapshot: plan fetch failed for company {CompanyId}.", companyId); }

                    await UpsertAsync(db, customer, year, month, summary, plan, now, ct);
                    captured++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "UsageSnapshot: capture failed for customer {CustomerId} (company {CompanyId}).",
                        customer.Id, companyId);
                }
                finally
                {
                    // Reset change tracking each iteration so a failed SaveChanges can't leave a
                    // poisoned entity that re-fails (and drops) every subsequent customer.
                    db.ChangeTracker.Clear();
                }
            }

            _logger.LogInformation("UsageSnapshot: done. captured={Captured} failed={Failed}.", captured, failed);
        }
        finally
        {
            try { await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})"); }
            catch { /* connection gone — session lock dies with it */ }
        }
    }

    /// <summary>Upserts one (customer, year, month) snapshot row idempotently.</summary>
    private static async Task UpsertAsync(
        ApplicationDbContext db,
        Customer customer,
        int year,
        int month,
        Application.Common.Models.ExternalApis.EmsCompanySummaryResponse summary,
        Application.Common.Models.ExternalApis.LiftdeskCompanyPlan? plan,
        DateTime now,
        CancellationToken ct)
    {
        var m = summary.Monthly.FirstOrDefault(x => x.Year == year && x.Month == month);

        // Plan monthly price: match the current plan against the available-plans price list.
        decimal? planPrice = null;
        if (plan?.Current is { } cur)
        {
            var match = plan.AvailablePlans.FirstOrDefault(p => p.PlanId == cur.PlanId)
                        ?? plan.AvailablePlans.FirstOrDefault(p =>
                            string.Equals(p.Tier, cur.Tier, StringComparison.OrdinalIgnoreCase));
            planPrice = match?.PriceMonthly;
        }

        var row = await db.CustomerUsageSnapshots
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.CustomerId == customer.Id
                                   && s.SnapshotYear == year
                                   && s.SnapshotMonth == month, ct);

        var isNew = row is null;
        row ??= new CustomerUsageSnapshot
        {
            ProjectId     = customer.ProjectId,
            CustomerId    = customer.Id,
            SnapshotYear  = year,
            SnapshotMonth = month,
        };

        row.ElevatorCount        = summary.Totals.ElevatorCount;
        row.UserCount            = summary.Totals.UserCount;
        // LastLoginAt: not on the wire DTO yet (Liftdesk gap) — stays null until it ships.
        row.MaintenanceCount     = m?.MaintenanceCount ?? 0;
        row.FaultCount           = m?.FaultCount ?? 0;
        row.PartChangeOfferCount = m?.PartChangeOfferCount ?? 0;
        row.RevisionOfferCount   = m?.RevisionOfferCount ?? 0;
        row.AssemblyOfferCount   = m?.AssemblyOfferCount ?? 0;
        // WorkOrderCount: not on the wire DTO yet (Liftdesk gap) — stays 0 until it ships.
        row.PlanTier             = plan?.Current?.Tier;
        row.PlanStatus           = plan?.Current?.Status;
        row.PlanMonthlyPrice     = planPrice;
        row.ExpirationDate       = customer.ExpirationDate;
        row.CapturedAt           = now;

        if (isNew) db.CustomerUsageSnapshots.Add(row);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> TryAdvisoryLockAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_try_advisory_lock({AdvisoryLockKey})";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }
}
