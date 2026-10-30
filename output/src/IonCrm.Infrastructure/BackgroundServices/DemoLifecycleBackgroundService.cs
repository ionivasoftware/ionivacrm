using IonCrm.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>Every LifecycleMail:IntervalMinutes (60): due demo lifecycle e-mails (welcome / check-in / trial ended) for non-TR Liftdesk trials.</summary>
public sealed class DemoLifecycleBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoLifecycleBackgroundService> _logger;

    public DemoLifecycleBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<DemoLifecycleBackgroundService> logger)
    {
        _scopeFactory = scopeFactory; _configuration = configuration; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("LifecycleMail:IntervalMinutes", 60), 5, 720));
        _logger.LogInformation("DemoLifecycleBackgroundService started — every {Minutes} min.", interval.TotalMinutes);
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IDemoLifecycleService>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Demo lifecycle: cycle failed."); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
