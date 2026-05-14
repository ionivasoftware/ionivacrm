using IonCrm.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Timer-based background service that executes <see cref="SaasSyncJob"/> every 30 minutes
/// during business hours (09:00–19:00 Turkey local, UTC+3).  Used as the primary sync scheduler
/// when Hangfire is not enabled (<c>Hangfire:Enabled = false</c>).
///
/// Outside business hours the cycle is a pure no-op — no advisory-lock connection is opened
/// and no DB query runs.  This lets Neon's serverless compute auto-suspend, dramatically
/// reducing the dev compute bill.
///
/// EMS payment lookback window:
///   • Normal cycle (last sync ≤ 2 hours ago)  → 60 minutes  (covers the 30-min cycle + buffer)
///   • Long gap   (first run of day, restart)  → 16 hours    (catches anything written overnight)
///
/// Railway single-replica gotcha: even without Hangfire, Railway may briefly run two containers
/// during a rolling deploy. A PostgreSQL session-level advisory lock guarantees only one instance
/// executes the sync at a time.  The lock is released automatically when the DB connection closes
/// (end of each sync cycle, or on crash / connection drop).
///
/// The 30-second startup delay gives EF Core migrations time to finish before the first sync.
/// </summary>
public sealed class SyncTimerService : BackgroundService
{
    /// <summary>How often the sync cycle fires.</summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Turkey local-time business window during which sync cycles run.
    /// Outside this range cycles are no-ops so Neon compute can auto-suspend.
    /// </summary>
    private const int BusinessStartHourTrt = 9;   // 09:00 TRT
    private const int BusinessEndHourTrt   = 19;  // 19:00 TRT (exclusive)

    /// <summary>UTC offset of Turkey local time. Stable: Türkiye has had no DST since 2016.</summary>
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    /// <summary>EMS payment lookback used during normal cycles.</summary>
    private const int NormalEmsWindowMinutes = 60;

    /// <summary>EMS payment lookback used after a long gap (first cycle after the overnight pause).</summary>
    private const int LongGapEmsWindowMinutes = 16 * 60;   // 16 hours

    /// <summary>Threshold beyond which the next cycle is considered "after a long gap".</summary>
    private static readonly TimeSpan LongGapThreshold = TimeSpan.FromHours(2);

    /// <summary>
    /// Arbitrary unique int64 key for <c>pg_try_advisory_lock</c>.
    /// Must not collide with any other advisory lock in the application.
    /// </summary>
    private const long AdvisoryLockKey = 7_391_827_364_918_273L;

    /// <summary>Delay before the first sync so DB migrations can complete.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration       _configuration;
    private readonly ILogger<SyncTimerService> _logger;

    /// <summary>UTC time of the last successful sync cycle.  Null until the first one completes.</summary>
    private DateTime? _lastSyncedAtUtc;

    /// <summary>Initialises a new instance of <see cref="SyncTimerService"/>.</summary>
    public SyncTimerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration       configuration,
        ILogger<SyncTimerService> logger)
    {
        _scopeFactory  = scopeFactory;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SyncTimerService started — cycle every {Minutes} min during {Start:00}:00–{End:00}:00 TRT.",
            SyncInterval.TotalMinutes, BusinessStartHourTrt, BusinessEndHourTrt);

        // Brief startup delay so EF migrations finish before the first sync attempt.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // App is shutting down before first sync — fine.
        }

        // First sync immediately after startup delay.
        await RunSyncWithLockAsync(stoppingToken);

        // Subsequent syncs on the configured interval.
        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSyncWithLockAsync(stoppingToken);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a PostgreSQL advisory lock, runs the sync, then releases the lock.
    /// Skips the cycle silently if another replica already holds the lock OR if the
    /// current Turkey local time is outside the business window.
    /// </summary>
    private async Task RunSyncWithLockAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        // Outside business hours → no-op so Neon can suspend.
        if (!IsInsideBusinessHours())
            return;

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError(
                "SyncTimerService: DefaultConnection string is missing — cannot run sync.");
            return;
        }

        // Dedicated connection for the advisory lock lifetime.
        // Disposing the connection automatically releases the session-level lock.
        await using var lockConn = new NpgsqlConnection(connectionString);
        try
        {
            await lockConn.OpenAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "SyncTimerService: could not open advisory-lock connection — skipping this cycle.");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // pg_try_advisory_lock returns TRUE if the lock was acquired, FALSE if another
        // session already holds it (non-blocking — never waits).
        bool lockAcquired;
        try
        {
            await using var lockCmd = new NpgsqlCommand(
                $"SELECT pg_try_advisory_lock({AdvisoryLockKey})", lockConn);
            lockAcquired = (bool)(await lockCmd.ExecuteScalarAsync(ct))!;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "SyncTimerService: advisory lock query failed — skipping this cycle.");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!lockAcquired)
        {
            // Skip silently — another replica is mid-sync. Not noteworthy.
            return;
        }

        // Calculate EMS payment window based on time since last successful sync.
        // First-of-day / after-restart → look back far enough to catch overnight payments.
        int emsWindowMinutes = NormalEmsWindowMinutes;
        if (_lastSyncedAtUtc is null || DateTime.UtcNow - _lastSyncedAtUtc.Value > LongGapThreshold)
        {
            emsWindowMinutes = LongGapEmsWindowMinutes;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var syncJob = scope.ServiceProvider.GetRequiredService<SaasSyncJob>();
            await syncJob.RunAsync(emsWindowMinutes, ct);

            _lastSyncedAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("SyncTimerService: sync cancelled — graceful shutdown.");
        }
        catch (Exception ex)
        {
            // SaasSyncJob already logs and persists errors internally.
            // Log at Error here as well so this is visible in Railway logs.
            _logger.LogError(ex,
                "SyncTimerService: unexpected error during sync cycle.");
        }
        finally
        {
            // Explicit advisory unlock (belt-and-suspenders — the connection dispose also releases it).
            try
            {
                await using var unlockCmd = new NpgsqlCommand(
                    $"SELECT pg_advisory_unlock({AdvisoryLockKey})", lockConn);
                await unlockCmd.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SyncTimerService: explicit advisory unlock failed " +
                    "(lock will be released when the connection closes).");
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the current UTC time, shifted to Turkey local (UTC+3),
    /// falls within [BusinessStartHourTrt, BusinessEndHourTrt).
    /// </summary>
    private static bool IsInsideBusinessHours()
    {
        int localHour = (DateTime.UtcNow + TurkeyOffset).Hour;
        return localHour >= BusinessStartHourTrt && localHour < BusinessEndHourTrt;
    }
}
