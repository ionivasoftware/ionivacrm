using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Watches Liftdesk backup health server-side and records every state change.
///
/// WHY A BACKGROUND SERVICE: the dashboard card only queries while somebody has the screen open,
/// but the failure worth catching is the one nobody is looking at — "yedek alınmadığını felaket
/// anında öğrenmek" (contract §7.3). Polling here is what makes silent failure loud.
///
/// Writes ONLY on transition (healthy ↔ unhealthy), so the table stays a readable timeline and the
/// log is not spammed once per poll while an outage persists. A flip to unhealthy is logged at
/// Error level with the reasons attached, which is what a log-based alert would trigger on.
///
/// "Sessizlik başarı değildir": when the source cannot be reached at all we deliberately do NOT
/// treat that as healthy — it is left as an explicit unknown (warning, no state change), because
/// silently assuming health is the exact mistake this feature exists to prevent.
/// </summary>
public sealed class BackupHealthMonitorService : BackgroundService
{
    /// <summary>Distinct advisory-lock key — must not collide with the other background services.</summary>
    private const long AdvisoryLockKey = 7_391_827_364_918_307L;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>Startup delay so the idempotent SQL bootstrap (which creates the table) finishes first.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupHealthMonitorService> _logger;

    /// <summary>Initialises a new instance of <see cref="BackupHealthMonitorService"/>.</summary>
    public BackupHealthMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<BackupHealthMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackupHealthMonitorService started — checking every {Minutes} min.",
            Interval.TotalMinutes);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupHealthMonitor: check cycle failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<ILiftdeskBackupClient>();

        if (!client.IsConfigured)
        {
            _logger.LogDebug("BackupHealthMonitor: Liftdesk API key not configured — skipping.");
            return;
        }

        // Single-writer guard across rolling-deploy containers; without it two instances could each
        // record the same transition.
        if (!await TryAdvisoryLockAsync(db, ct))
        {
            _logger.LogDebug("BackupHealthMonitor: advisory lock busy — another instance is checking.");
            return;
        }

        try
        {
            var envelope = await client.GetStatusAsync(ct);

            if (!envelope.Success || envelope.Data is null)
            {
                // Unreachable source is NOT evidence of health. Warn and leave the recorded state
                // untouched so a real outage is not masked by a transport failure (and so a flapping
                // connection does not manufacture fake recoveries).
                _logger.LogWarning(
                    "BackupHealthMonitor: yedek durumu alınamadı — durum BİLİNMİYOR (sağlıklı sayılmadı). {Message}",
                    envelope.Message ?? "(mesaj yok)");
                return;
            }

            var status = envelope.Data;

            var last = await db.BackupHealthEvents
                .IgnoreQueryFilters()
                .Where(e => !e.IsDeleted)
                .OrderByDescending(e => e.DetectedAt)
                .FirstOrDefaultAsync(ct);

            // First observation counts as a transition so the timeline always has a starting point.
            if (last is not null && last.IsHealthy == status.IsHealthy)
            {
                if (!status.IsHealthy)
                {
                    _logger.LogWarning(
                        "BackupHealthMonitor: yedekleme hâlâ sorunlu ({Since:u}'dan beri). Sebepler: {Problems}",
                        last.DetectedAt, Join(status.Problems));
                }
                return;
            }

            var problems = Join(status.Problems);

            db.BackupHealthEvents.Add(new BackupHealthEvent
            {
                IsHealthy = status.IsHealthy,
                Problems  = status.IsHealthy ? null : problems,
                HoursSinceLastSuccessfulBackup = status.HoursSinceLastSuccessfulBackup,
                DetectedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);

            if (status.IsHealthy)
            {
                _logger.LogInformation("BackupHealthMonitor: yedekleme DÜZELDİ.");
            }
            else
            {
                // Error level on purpose: this is the line a log-based alert should fire on.
                _logger.LogError(
                    "BackupHealthMonitor: YEDEKLEME SORUNLU. Son başarılı yedek: {Hours} saat önce. Sebepler: {Problems}",
                    status.HoursSinceLastSuccessfulBackup?.ToString("0.#") ?? "bilinmiyor",
                    problems);
            }
        }
        finally
        {
            try { await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})"); }
            catch { /* connection gone — session lock dies with it */ }
        }
    }

    private static string Join(List<string>? problems) =>
        problems is { Count: > 0 } ? string.Join("\n", problems) : "(sebep bildirilmedi)";

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
