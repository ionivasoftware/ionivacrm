using IonCrm.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>Every Outreach:IntervalMinutes (10) runs one outreach cycle: due global leads → inbox check → send within window/cap.</summary>
public sealed class OutreachBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutreachBackgroundService> _logger;

    public OutreachBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OutreachBackgroundService> logger)
    {
        _scopeFactory = scopeFactory; _configuration = configuration; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("Outreach:IntervalMinutes", 10), 2, 240));
        _logger.LogInformation("OutreachBackgroundService started — every {Minutes} min.", interval.TotalMinutes);
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IOutreachService>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Outreach: cycle failed."); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
