using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Runs the CSO planner once per local day for every project, from CallPlan:RunHourLocal (05:00 TR)
/// onwards — so the week's calls are on the calendar before anyone sits down. Checks every 20 minutes;
/// the service itself is idempotent per (project, day), so a restart or a second container cannot plan
/// twice (advisory lock guards the concurrent case as well). A short startup delay lets the SQL
/// bootstrap create the tables first; after a deploy the plan appears within minutes.
/// </summary>
public sealed class CallPlanBackgroundService : BackgroundService
{
    private const long AdvisoryLockKey = 5_570_118_223_904_117L;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CallPlanBackgroundService> _logger;

    public CallPlanBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<CallPlanBackgroundService> logger)
    {
        _scopeFactory = scopeFactory; _configuration = configuration; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("CallPlan:Enabled", true))
        {
            _logger.LogInformation("CallPlanBackgroundService disabled (CallPlan:Enabled=false).");
            return;
        }
        _logger.LogInformation("CallPlanBackgroundService started — daily from {Hour}:00 local, checking every {Minutes} min.",
            _configuration.GetValue("CallPlan:RunHourLocal", 5), Interval.TotalMinutes);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "CallPlan: cycle failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var tz = SocialPublishPlanner.ResolveTimeZone(_configuration["CallPlan:TimeZone"]);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        if (nowLocal.Hour < _configuration.GetValue("CallPlan:RunHourLocal", 5)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await TryAdvisoryLockAsync(db, ct)) return;

        var service = scope.ServiceProvider.GetRequiredService<ICallPlanService>();
        var results = await service.RunAllAsync(force: false, ct);
        var created = results.Sum(r => r.Created);
        if (created > 0 || results.Any(r => !r.AlreadyRan))
            _logger.LogInformation("CallPlan: {Projects} proje, {Created} yeni randevu.", results.Count(r => !r.AlreadyRan), created);
    }

    private static async Task<bool> TryAdvisoryLockAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_try_advisory_lock({AdvisoryLockKey})";
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
