using IonCrm.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Timer for the social-media publisher: runs <see cref="ISocialPublishRunner"/> every
/// SocialPublisher:IntervalMinutes (default 30; the contract says hourly, a shorter tick only
/// tightens how close to the publish hour a post goes out). Startup delay lets the idempotent SQL
/// bootstrap create the tables first. Concurrency across containers is handled inside the runner
/// (advisory lock), so the manual "run now" endpoint can share the same code path.
/// </summary>
public sealed class SocialPublisherService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialPublisherService> _logger;

    public SocialPublisherService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<SocialPublisherService> logger)
    {
        _scopeFactory = scopeFactory; _configuration = configuration; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(_configuration.GetValue("SocialPublisher:IntervalMinutes", 30), 5, 24 * 60);
        var interval = TimeSpan.FromMinutes(minutes);
        _logger.LogInformation("SocialPublisherService started — every {Minutes} min.", minutes);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ISocialPublishRunner>();
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SocialPublisher: cycle failed.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
