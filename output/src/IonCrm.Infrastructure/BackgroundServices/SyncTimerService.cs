using IonCrm.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Timer-based background service that executes <see cref="SaasSyncJob"/> every 15 minutes.
/// Used as the primary sync scheduler when Hangfire is not enabled (Hangfire:Enabled = false).
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
    /// <summary>How often the sync runs.</summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

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
            "SyncTimerService started — running sync every {Minutes} minutes.",
            SyncInterval.TotalMinutes);

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

        // Subsequent syncs on the 15-minute interval.
        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSyncWithLockAsync(stoppingToken);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a PostgreSQL advisory lock, runs the sync, then releases the lock.
    /// Skips the cycle silently if another replica already holds the lock.
    /// </summary>
    private async Task RunSyncWithLockAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
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
            _logger.LogInformation(
                "SyncTimerService: advisory lock held by another instance — skipping this cycle.");
            return;
        }

        _logger.LogInformation(
            "SyncTimerService: advisory lock acquired. Starting sync at {Time:O}.",
            DateTime.UtcNow);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var syncJob = scope.ServiceProvider.GetRequiredService<SaasSyncJob>();
            await syncJob.RunAsync(ct);
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

                _logger.LogInformation(
                    "SyncTimerService: sync cycle complete, advisory lock released at {Time:O}.",
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SyncTimerService: explicit advisory unlock failed " +
                    "(lock will be released when the connection closes).");
            }
        }
    }
}
