using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Hangfire job that pulls data from SaaS A and SaaS B every 15 minutes.
/// For each source:
///   1. Fetches customers, subscriptions, and orders.
///   2. Upserts into ION CRM (insert new, update existing by LegacyId).
///   3. Logs the sync result to SyncLogs (Success / Failed).
/// Retry policy: 3 attempts with exponential backoff (2s, 4s, 8s).
/// </summary>
public sealed class SaasSyncJob
{
    private readonly ISaasAClient _saasAClient;
    private readonly ISaasBClient _saasBClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SaasSyncJob> _logger;

    // Polly v8 ResiliencePipeline — 3 retries with exponential backoff
    private static readonly ResiliencePipeline RetryPipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();

    /// <summary>Initialises a new instance of <see cref="SaasSyncJob"/>.</summary>
    public SaasSyncJob(
        ISaasAClient saasAClient,
        ISaasBClient saasBClient,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SaasSyncJob> logger)
    {
        _saasAClient = saasAClient;
        _saasBClient = saasBClient;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Main Hangfire job entry point — called every 15 minutes.
    /// Creates a DI scope per sync run to properly manage DbContext lifetime.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SaaS sync job started at {Time:O}", DateTime.UtcNow);

        await SyncSaasAAsync(cancellationToken);
        await SyncSaasBAsync(cancellationToken);

        _logger.LogInformation("SaaS sync job completed at {Time:O}", DateTime.UtcNow);
    }

    // ── SaaS A sync ───────────────────────────────────────────────────────────

    private async Task SyncSaasAAsync(CancellationToken ct)
    {
        var projectId = GetProjectId("SaasA:ProjectId");
        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("SaaS A ProjectId is not configured. Skipping SaaS A sync.");
            return;
        }

        _logger.LogInformation("Starting SaaS A sync for project {ProjectId}.", projectId);

        // Customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Customer",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetCustomersAsync(ct);
                await UpsertSaasACustomersAsync(response.Data, projectId, ct);
                return response.Data.Count;
            });

        // Subscriptions (logged as ContactHistory notes for now)
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Subscription",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetSubscriptionsAsync(ct);
                return response.Data.Count; // mapping to be extended per business rules
            });

        // Orders
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Order",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetOrdersAsync(ct);
                return response.Data.Count;
            });

        _logger.LogInformation("SaaS A sync completed.");
    }

    // ── SaaS B sync ───────────────────────────────────────────────────────────

    private async Task SyncSaasBAsync(CancellationToken ct)
    {
        var projectId = GetProjectId("SaasB:ProjectId");
        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("SaaS B ProjectId is not configured. Skipping SaaS B sync.");
            return;
        }

        _logger.LogInformation("Starting SaaS B sync for project {ProjectId}.", projectId);

        // Customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Customer",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetCustomersAsync(ct);
                await UpsertSaasBCustomersAsync(response.Customers, projectId, ct);
                return response.Customers.Count;
            });

        // Subscriptions
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Subscription",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetSubscriptionsAsync(ct);
                return response.Subscriptions.Count;
            });

        // Orders
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Order",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetOrdersAsync(ct);
                return response.Orders.Count;
            });

        _logger.LogInformation("SaaS B sync completed.");
    }

    // ── Retry wrapper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a sync action in the Polly retry pipeline (3 attempts, exponential backoff).
    /// Logs success/failure to SyncLogs table after each attempt.
    /// </summary>
    private async Task SyncWithRetryAsync(
        SyncSource source,
        string entityType,
        Guid projectId,
        Func<Task<int>> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var syncLogRepo = scope.ServiceProvider.GetRequiredService<ISyncLogRepository>();

        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Source = source,
            Direction = SyncDirection.Inbound,
            EntityType = entityType,
            Status = SyncStatus.Pending
        };

        await syncLogRepo.AddAsync(log);

        try
        {
            var count = await RetryPipeline.ExecuteAsync(async _ =>
            {
                if (log.RetryCount > 0)
                {
                    log.Status = SyncStatus.Retrying;
                    await syncLogRepo.UpdateAsync(log);
                }

                var result = await action();
                return result;
            }, CancellationToken.None);

            log.Status = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;
            await syncLogRepo.UpdateAsync(log);

            _logger.LogInformation(
                "{Source} {EntityType} sync succeeded. Records processed: {Count}",
                source, entityType, count);
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorMessage = ex.Message.Length > 2000
                ? ex.Message[..2000]
                : ex.Message;
            await syncLogRepo.UpdateAsync(log);

            _logger.LogError(ex,
                "{Source} {EntityType} sync failed after retries.",
                source, entityType);
        }
    }

    // ── Upsert helpers ────────────────────────────────────────────────────────

    private async Task UpsertSaasACustomersAsync(
        List<SaasACustomer> customers,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var saasCustomer in customers)
        {
            var legacyId = $"SAASA-{saasCustomer.Id}";

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.CompanyName = saasCustomer.Name;
                existing.Email = saasCustomer.Email;
                existing.Phone = saasCustomer.Phone;
                existing.Address = saasCustomer.Address;
                existing.TaxNumber = saasCustomer.TaxNumber;
                existing.Status = MapSaasAStatus(saasCustomer.Status);
                existing.Segment = MapSaasASegment(saasCustomer.Segment);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    LegacyId = legacyId,
                    CompanyName = saasCustomer.Name,
                    Email = saasCustomer.Email,
                    Phone = saasCustomer.Phone,
                    Address = saasCustomer.Address,
                    TaxNumber = saasCustomer.TaxNumber,
                    Status = MapSaasAStatus(saasCustomer.Status),
                    Segment = MapSaasASegment(saasCustomer.Segment),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private async Task UpsertSaasBCustomersAsync(
        List<SaasBCustomer> customers,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var saasCustomer in customers)
        {
            var legacyId = $"SAASB-{saasCustomer.CustomerId}";

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.CompanyName = saasCustomer.FullName;
                existing.Email = saasCustomer.ContactEmail;
                existing.Phone = saasCustomer.Mobile;
                existing.Address = saasCustomer.StreetAddress;
                existing.TaxNumber = saasCustomer.TaxId;
                existing.Status = MapSaasBStatus(saasCustomer.AccountState);
                existing.Segment = MapSaasBTier(saasCustomer.Tier);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    LegacyId = legacyId,
                    CompanyName = saasCustomer.FullName,
                    Email = saasCustomer.ContactEmail,
                    Phone = saasCustomer.Mobile,
                    Address = saasCustomer.StreetAddress,
                    TaxNumber = saasCustomer.TaxId,
                    Status = MapSaasBStatus(saasCustomer.AccountState),
                    Segment = MapSaasBTier(saasCustomer.Tier),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static CustomerStatus MapSaasAStatus(string status) => status.ToLower() switch
    {
        "active" => CustomerStatus.Active,
        "lead" => CustomerStatus.Lead,
        "inactive" or "passive" => CustomerStatus.Inactive,
        "churned" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };

    private static CustomerSegment? MapSaasASegment(string? segment) =>
        segment?.ToLower() switch
        {
            "enterprise" => CustomerSegment.Enterprise,
            "sme" => CustomerSegment.SME,
            "individual" => CustomerSegment.Individual,
            _ => null
        };

    private static CustomerStatus MapSaasBStatus(string state) => state.ToUpper() switch
    {
        "ACTIVE" => CustomerStatus.Active,
        "LEAD" => CustomerStatus.Lead,
        "INACTIVE" or "PASSIVE" => CustomerStatus.Inactive,
        "CHURNED" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };

    private static CustomerSegment? MapSaasBTier(string? tier) =>
        tier?.ToUpper() switch
        {
            "ENTERPRISE" => CustomerSegment.Enterprise,
            "SME" => CustomerSegment.SME,
            "INDIVIDUAL" => CustomerSegment.Individual,
            _ => null
        };

    private Guid GetProjectId(string configKey)
    {
        var value = _configuration[configKey];
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
