using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Hosted service that registers the Hangfire recurring sync job on startup.
/// The actual sync logic lives in <see cref="SaasSyncJob"/>.
/// Runs every 15 minutes: SaaS A + SaaS B → CRM upsert.
/// </summary>
public sealed class SyncBackgroundService : IHostedService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<SyncBackgroundService> _logger;

    /// <summary>Initialises a new instance of <see cref="SyncBackgroundService"/>.</summary>
    public SyncBackgroundService(
        IRecurringJobManager recurringJobManager,
        ILogger<SyncBackgroundService> logger)
    {
        _recurringJobManager = recurringJobManager;
        _logger = logger;
    }

    /// <summary>
    /// Registers the Hangfire recurring job on application startup.
    /// CRON "*/15 * * * *" → every 15 minutes.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering SaaS sync recurring job (every 15 minutes).");

        // Register the recurring Hangfire job — Hangfire manages its own execution schedule
        _recurringJobManager.AddOrUpdate<SaasSyncJob>(
            recurringJobId: "saas-full-sync",
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: "*/15 * * * *",  // every 15 minutes
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        _logger.LogInformation("SaaS sync recurring job registered successfully.");
        return Task.CompletedTask;
    }

    /// <summary>No cleanup required — Hangfire manages its own lifecycle.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SyncBackgroundService stopping.");
        return Task.CompletedTask;
    }
}
