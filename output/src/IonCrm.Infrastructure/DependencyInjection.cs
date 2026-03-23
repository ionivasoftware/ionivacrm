using Hangfire;
using Hangfire.PostgreSql;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.ExternalApis;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure;

/// <summary>
/// Extension methods for registering all Infrastructure-layer services into the DI container.
/// Call this from Program.cs: <c>services.AddInfrastructureServices(configuration)</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core DbContext, repositories, external API clients,
    /// Hangfire, and background services.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Database ──────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. Set via environment variable.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // ── Current user from JWT claims ──────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // ── Migration service (Singleton — maintains state across requests) ───
        // DataMigrationService uses IServiceScopeFactory to resolve scoped DbContext internally.
        services.AddSingleton<IMigrationService>(sp =>
            new DataMigrationService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<DataMigrationService>>()));

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IContactHistoryRepository, ContactHistoryRepository>();
        services.AddScoped<ICustomerTaskRepository, CustomerTaskRepository>();
        services.AddScoped<ISyncLogRepository, SyncLogRepository>();

        // ── External API Clients (Typed HttpClients) ──────────────────────────
        RegisterSaasAClient(services, configuration);
        RegisterSaasBClient(services, configuration);

        // ── Hangfire (background job scheduler + server) ──────────────────────
        services.AddHangfire(cfg =>
        {
            cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
               .UseSimpleAssemblyNameTypeSerializer()
               .UseRecommendedSerializerSettings()
               .UsePostgreSqlStorage(options =>
               {
                   options.UseNpgsqlConnection(connectionString);
               });
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;
            options.Queues = new[] { "default" };
        });

        // ── Background service: registers Hangfire recurring jobs ─────────────
        services.AddHostedService<SyncBackgroundService>();

        // Register the sync job class for Hangfire DI activation
        services.AddScoped<SaasSyncJob>();

        return services;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void RegisterSaasAClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["SaasA:BaseUrl"];
        var apiKey = configuration["SaasA:ApiKey"];

        services.AddHttpClient<ISaasAClient, SaasAClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    private static void RegisterSaasBClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["SaasB:BaseUrl"];
        var apiKey = configuration["SaasB:ApiKey"];

        services.AddHttpClient<ISaasBClient, SaasBClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}
