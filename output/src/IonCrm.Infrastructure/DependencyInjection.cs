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
using Polly;
using Polly.Extensions.Http;

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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IContactHistoryRepository, ContactHistoryRepository>();
        services.AddScoped<ICustomerTaskRepository, CustomerTaskRepository>();
        services.AddScoped<ISyncLogRepository, SyncLogRepository>();
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();

        // ── Auth services ─────────────────────────────────────────────────────
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // ── Paraşüt ───────────────────────────────────────────────────────────
        services.AddScoped<IParasutConnectionRepository, ParasutConnectionRepository>();
        RegisterParasutClient(services);

        // High-level facade: wraps connection lookup + token lifecycle + IParasutClient calls.
        // Handlers inject IParasutService instead of wiring IParasutClient + ParasutTokenHelper manually.
        services.AddScoped<IParasutService, ParasutService>();

        // On startup: refresh/re-authenticate all stored Paraşüt connections whose tokens
        // have expired, so the first user API call doesn't trigger a slow re-auth round-trip.
        services.AddHostedService<ParasutAutoConnectService>();

        // ── External API Clients (Typed HttpClients) ──────────────────────────
        RegisterSaasAClient(services, configuration);
        RegisterSaasBClient(services, configuration);

        // SaasSyncJob is always registered so the trigger endpoint can run it directly
        // regardless of whether Hangfire is enabled.
        services.AddScoped<SaasSyncJob>();

        // ── Hangfire (background job scheduler + server) ──────────────────────
        var enableHangfire = configuration.GetValue<bool>("Hangfire:Enabled", false);
        if (enableHangfire)
        {
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
        }

        return services;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Polly v7 circuit-breaker policy (Polly.Extensions.Http) for the HttpClient
    /// transport layer.  This is complementary to — NOT a replacement for — the Polly v8
    /// <c>ResiliencePipeline</c> inside each typed client (SaasAClient / SaasBClient):
    ///
    ///   Layer 1 — Typed client (Polly v8 ResiliencePipeline)
    ///     • 3 retries, exponential back-off 2 s / 4 s / 8 s (± jitter)
    ///     • Handles HttpRequestException + TaskCanceledException
    ///     • Logs each retry attempt via ILogger
    ///
    ///   Layer 2 — HttpClient handler pipeline (Polly.Extensions.Http, this method)
    ///     • Circuit-breaker: opens after 5 consecutive failures (HTTP 5xx / 408 / network)
    ///     • Stays open for 30 seconds, then allows one probe request
    ///     • Prevents cascading failures when an entire SaaS endpoint is down
    ///
    /// The circuit-breaker ONLY triggers on the final outcome of all 4 attempts (initial + 3
    /// retries), so a single transient failure never trips the circuit.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> BuildCircuitBreakerPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()   // 5xx responses, 408, HttpRequestException
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));

    private static void RegisterParasutClient(IServiceCollection services)
    {
        // ParasutClient manages its own base URL per-call (token endpoint vs company endpoints)
        // so we register it with a plain HttpClient and no fixed base address.
        services
            .AddHttpClient<IParasutClient, ParasutClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.parasut.com/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddPolicyHandler(BuildCircuitBreakerPolicy());
    }

    private static void RegisterSaasAClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["SaasA:BaseUrl"];
        var apiKey  = configuration["SaasA:ApiKey"];

        services
            .AddHttpClient<ISaasAClient, SaasAClient>(client =>
            {
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

                // Bearer token auth — SaaS A standard REST convention
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                // Total timeout across ALL retry attempts.
                // Individual attempt backoff (2 s / 4 s / 8 s) sits inside this ceiling.
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler(BuildCircuitBreakerPolicy());
    }

    private static void RegisterSaasBClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["SaasB:BaseUrl"];
        var apiKey  = configuration["SaasB:ApiKey"];

        services
            .AddHttpClient<ISaasBClient, SaasBClient>(client =>
            {
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

                // X-Api-Key auth — SaaS B convention
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

                // Total timeout across ALL retry attempts.
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler(BuildCircuitBreakerPolicy());
    }
}
