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
    private readonly IProjectRepository _projectRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SaasSyncJob> _logger;

    // Retry pipeline is built per-call so OnRetry can close over the SyncLog instance
    // and update RetryCount + status in the database on each failed attempt.
    private ResiliencePipeline BuildRetryPipeline(SyncLog log, ISyncLogRepository syncLogRepo) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = async args =>
                {
                    log.RetryCount++;
                    log.Status = SyncStatus.Retrying;
                    log.ErrorMessage = args.Outcome.Exception?.Message;
                    await syncLogRepo.UpdateAsync(log);

                    _logger.LogWarning(
                        "Sync retry #{Attempt} for {Source}/{EntityType}. Error: {Error}",
                        log.RetryCount,
                        log.Source,
                        log.EntityType,
                        args.Outcome.Exception?.Message);
                }
            })
            .Build();

    /// <summary>Initialises a new instance of <see cref="SaasSyncJob"/>.</summary>
    public SaasSyncJob(
        ISaasAClient saasAClient,
        ISaasBClient saasBClient,
        IProjectRepository projectRepository,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SaasSyncJob> logger)
    {
        _saasAClient = saasAClient;
        _saasBClient = saasBClient;
        _projectRepository = projectRepository;
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

        await SyncEmsCrmCustomersAsync(cancellationToken);
        await SyncSaasAAsync(cancellationToken);
        await SyncSaasBAsync(cancellationToken);

        _logger.LogInformation("SaaS sync job completed at {Time:O}", DateTime.UtcNow);
    }

    // ── EMS CRM customers sync (new paginated endpoint) ───────────────────────

    /// <summary>
    /// Syncs customers from the EMS /api/v1/crm/customers endpoint.
    /// Delta sync: uses the last successful CrmCustomer sync time as updatedSince (with 5-min buffer).
    /// First-ever sync (or if last sync was >24 h ago): full sync with no updatedSince filter.
    /// </summary>
    private async Task SyncEmsCrmCustomersAsync(CancellationToken ct)
    {
        var (projectId, project) = await ResolveProjectAsync(
            "SaasA:ProjectId", p => !string.IsNullOrEmpty(p.EmsApiKey), ct);

        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("No project found for EMS CRM sync. Skipping.");
            return;
        }

        var emsApiKey = project?.EmsApiKey;

        // Determine delta window: find the last successful sync time in SyncLogs.
        // Use IgnoreQueryFilters() — background job has no HTTP user context, so the
        // tenant query filter would block all rows otherwise.
        DateTime? updatedSince = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lastSync = await context.SyncLogs
                .IgnoreQueryFilters()
                .Where(l => l.ProjectId == projectId
                         && l.Source == SyncSource.SaasA
                         && l.EntityType == "CrmCustomer"
                         && l.Status == SyncStatus.Success
                         && l.SyncedAt != null)
                .OrderByDescending(l => l.SyncedAt)
                .Select(l => l.SyncedAt)
                .FirstOrDefaultAsync(ct);

            if (lastSync.HasValue && lastSync.Value > DateTime.UtcNow.AddHours(-24))
            {
                updatedSince = lastSync.Value.AddMinutes(-5);
                _logger.LogInformation("EMS CRM delta sync: updatedSince={Since:u}", updatedSince);
            }
            else
            {
                _logger.LogInformation(
                    "EMS CRM full sync: no recent successful sync found, fetching all records.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not determine last sync time — proceeding with full sync.");
        }

        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "CrmCustomer",
            projectId: projectId,
            action: async () =>
            {
                const int pageSize = 500;
                int page = 1, totalSynced = 0;

                while (true)
                {
                    var response = await _saasAClient.GetCrmCustomersPageAsync(
                        emsApiKey, page, pageSize, updatedSince, ct);

                    if (response.Data.Count == 0)
                        break;

                    await UpsertEmsCrmCustomersAsync(response.Data, projectId, ct);
                    totalSynced += response.Data.Count;

                    if (page >= response.TotalPages || response.TotalPages == 0)
                        break;

                    page++;
                }

                return totalSynced;
            });
    }

    private async Task UpsertEmsCrmCustomersAsync(
        List<EmsCrmCustomer> customers,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var src in customers)
        {
            // Canonical LegacyId = plain numeric EMS company ID (matches original DB migration).
            // Also check the old "SAASA-{id}" prefix format in case records were created by a
            // previous sync run before this normalization was applied.
            var legacyId         = src.Id;              // "3"
            var legacyIdPrefixed = $"SAASA-{src.Id}";  // "SAASA-3" (legacy)
            var newStatus = ComputeStatusFromExpiration(src.ExpirationDate, src.CreatedOn);

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted
                         && (c.LegacyId == legacyId || c.LegacyId == legacyIdPrefixed))
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // Normalize to canonical format if it was stored with old prefix
                if (existing.LegacyId == legacyIdPrefixed)
                    existing.LegacyId = legacyId;

                bool changed = existing.LegacyId != legacyIdPrefixed; // true if we just normalized
                if (existing.CompanyName    != src.Name)           { existing.CompanyName    = src.Name;           changed = true; }
                if (existing.Email          != src.Email)          { existing.Email          = src.Email;          changed = true; }
                if (existing.Phone          != src.Phone)          { existing.Phone          = src.Phone;          changed = true; }
                if (existing.Address        != src.Address)        { existing.Address        = src.Address;        changed = true; }
                if (existing.TaxNumber      != src.TaxNumber)      { existing.TaxNumber      = src.TaxNumber;      changed = true; }
                if (existing.Segment        != src.Segment)        { existing.Segment        = src.Segment;        changed = true; }
                if (existing.ExpirationDate != src.ExpirationDate) { existing.ExpirationDate = src.ExpirationDate; changed = true; }
                if (existing.Status         != newStatus)          { existing.Status         = newStatus;          changed = true; }
                if (changed) existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id             = Guid.NewGuid(),
                    ProjectId      = projectId,
                    LegacyId       = legacyId,   // plain numeric — canonical format
                    CompanyName    = src.Name,
                    Email          = src.Email,
                    Phone          = src.Phone,
                    Address        = src.Address,
                    TaxNumber      = src.TaxNumber,
                    Segment        = src.Segment,
                    ExpirationDate = src.ExpirationDate,
                    Status         = newStatus,
                    CreatedAt      = src.CreatedOn,
                    UpdatedAt      = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Computes customer status based on ExpirationDate rules:
    /// <list type="bullet">
    ///   <item>Active:   not expired AND (created &gt;30 days ago OR expires &gt;30 days from now)</item>
    ///   <item>Demo:     not expired AND expires within 30 days AND created within last 30 days</item>
    ///   <item>Churned:  expired AND had subscription for &gt;40 days</item>
    ///   <item>Passive:  expired AND had subscription for ≤40 days</item>
    /// </list>
    /// </summary>
    private static CustomerStatus ComputeStatusFromExpiration(DateTime? expirationDate, DateTime createdOn)
    {
        if (!expirationDate.HasValue)
            return CustomerStatus.Lead;

        var today   = DateTime.UtcNow.Date;
        var exp     = expirationDate.Value.Date;
        var created = createdOn.Date;

        if (exp >= today)
        {
            // Not expired
            bool expiresWithin30Days  = exp < today.AddDays(30);
            bool createdWithinLast30  = created > today.AddDays(-30);

            if (expiresWithin30Days && createdWithinLast30)
                return CustomerStatus.Demo;

            return CustomerStatus.Active;
        }
        else
        {
            // Expired
            return created.AddDays(40) < exp
                ? CustomerStatus.Churned   // had subscription for >40 days
                : CustomerStatus.Passive;  // never fully onboarded (≤40 days)
        }
    }

    // ── SaaS A sync ───────────────────────────────────────────────────────────

    private async Task SyncSaasAAsync(CancellationToken ct)
    {
        var (projectId, project) = await ResolveProjectAsync(
            "SaasA:ProjectId", p => !string.IsNullOrEmpty(p.EmsApiKey), ct);

        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("No project found for SaaS A (EMS) sync. Skipping.");
            return;
        }

        var emsApiKey = project?.EmsApiKey;

        _logger.LogInformation(
            "Starting SaaS A (EMS) sync for project {ProjectId}. ApiKey configured: {HasKey}",
            projectId, emsApiKey is not null);

        // Customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Customer",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetCustomersAsync(emsApiKey, ct);
                await UpsertSaasACustomersAsync(response.Data, projectId, ct);
                return response.Data.Count;
            });

        // Subscriptions — update ExpirationDate on matching customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Subscription",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetSubscriptionsAsync(emsApiKey, ct);
                await UpdateSaasAExpirationDatesAsync(response.Data, ct);
                return response.Data.Count;
            });

        // Orders
        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "Order",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasAClient.GetOrdersAsync(emsApiKey, ct);
                return response.Data.Count;
            });

        _logger.LogInformation("SaaS A (EMS) sync completed.");
    }

    // ── SaaS B sync ───────────────────────────────────────────────────────────

    private async Task SyncSaasBAsync(CancellationToken ct)
    {
        var (projectId, project) = await ResolveProjectAsync(
            "SaasB:ProjectId", p => !string.IsNullOrEmpty(p.RezervAlApiKey), ct);

        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("No project found for SaaS B (Rezerval) sync. Skipping.");
            return;
        }

        var rezervAlApiKey = project?.RezervAlApiKey;

        _logger.LogInformation(
            "Starting SaaS B (Rezerval) sync for project {ProjectId}. ApiKey configured: {HasKey}",
            projectId, rezervAlApiKey is not null);

        // Customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Customer",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetCustomersAsync(rezervAlApiKey, ct);
                await UpsertSaasBCustomersAsync(response.Customers, projectId, ct);
                return response.Customers.Count;
            });

        // Subscriptions — update ExpirationDate on matching customers
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Subscription",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetSubscriptionsAsync(rezervAlApiKey, ct);
                await UpdateSaasBExpirationDatesAsync(response.Subscriptions, ct);
                return response.Subscriptions.Count;
            });

        // Orders
        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "Order",
            projectId: projectId,
            action: async () =>
            {
                var response = await _saasBClient.GetOrdersAsync(rezervAlApiKey, ct);
                return response.Orders.Count;
            });

        _logger.LogInformation("SaaS B sync completed.");
    }

    // ── Retry wrapper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a sync action in a Polly retry pipeline (3 attempts, exponential backoff).
    /// Uses a per-call pipeline so <see cref="SyncLog.RetryCount"/> is updated after
    /// each failed attempt via the <c>OnRetry</c> delegate before the next attempt.
    /// Logs final success/failure to the SyncLogs table.
    /// </summary>
    private async Task SyncWithRetryAsync(
        SyncSource source,
        string entityType,
        Guid projectId,
        Func<Task<int>> action)
    {
        using var scope = _scopeFactory.CreateScope();
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

        // Per-call pipeline closes over log+syncLogRepo to track each retry in DB
        var pipeline = BuildRetryPipeline(log, syncLogRepo);

        try
        {
            var count = await pipeline.ExecuteAsync<int>(
                async _ => await action(),
                CancellationToken.None);

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
                "{Source} {EntityType} sync failed after {Retries} retries.",
                source, entityType, log.RetryCount);
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
            var newStatus  = MapSaasAStatus(saasCustomer.Status);
            var newSegment = saasCustomer.Segment; // pass through as-is from SaaS

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // Only update fields that have actually changed (delta detection)
                bool changed = false;
                if (existing.CompanyName != saasCustomer.Name)           { existing.CompanyName = saasCustomer.Name;           changed = true; }
                if (existing.Email       != saasCustomer.Email)          { existing.Email       = saasCustomer.Email;          changed = true; }
                if (existing.Phone       != saasCustomer.Phone)          { existing.Phone       = saasCustomer.Phone;          changed = true; }
                if (existing.Address     != saasCustomer.Address)        { existing.Address     = saasCustomer.Address;        changed = true; }
                if (existing.TaxNumber   != saasCustomer.TaxNumber)      { existing.TaxNumber   = saasCustomer.TaxNumber;      changed = true; }
                if (existing.Status      != newStatus)                   { existing.Status      = newStatus;                   changed = true; }
                if (existing.Segment     != newSegment)                  { existing.Segment     = newSegment;                  changed = true; }
                if (changed) existing.UpdatedAt = DateTime.UtcNow;
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
                    Status = newStatus,
                    Segment = newSegment,
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
            var newStatus  = MapSaasBStatus(saasCustomer.AccountState);
            var newSegment = saasCustomer.Tier; // pass through as-is from SaaS

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // Only update fields that have actually changed (delta detection)
                bool changed = false;
                if (existing.CompanyName != saasCustomer.FullName)         { existing.CompanyName = saasCustomer.FullName;         changed = true; }
                if (existing.Email       != saasCustomer.ContactEmail)     { existing.Email       = saasCustomer.ContactEmail;     changed = true; }
                if (existing.Phone       != saasCustomer.Mobile)           { existing.Phone       = saasCustomer.Mobile;           changed = true; }
                if (existing.Address     != saasCustomer.StreetAddress)    { existing.Address     = saasCustomer.StreetAddress;    changed = true; }
                if (existing.TaxNumber   != saasCustomer.TaxId)            { existing.TaxNumber   = saasCustomer.TaxId;            changed = true; }
                if (existing.Status      != newStatus)                     { existing.Status      = newStatus;                     changed = true; }
                if (existing.Segment     != newSegment)                    { existing.Segment     = newSegment;                    changed = true; }
                if (changed) existing.UpdatedAt = DateTime.UtcNow;
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
                    Status = newStatus,
                    Segment = newSegment,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    // ── Subscription expiration sync ──────────────────────────────────────────

    private async Task UpdateSaasAExpirationDatesAsync(
        List<SaasASubscription> subscriptions,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Group by customer, take the latest ExpiresAt per customer (active subs first)
        var byCustomer = subscriptions
            .Where(s => s.ExpiresAt.HasValue)
            .GroupBy(s => s.CustomerId)
            .ToDictionary(
                g => $"SAASA-{g.Key}",
                g => g.OrderByDescending(s => s.ExpiresAt).First().ExpiresAt!.Value);

        foreach (var (legacyId, expiresAt) in byCustomer)
        {
            var customer = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (customer is not null && customer.ExpirationDate != expiresAt)
            {
                customer.ExpirationDate = expiresAt;
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private async Task UpdateSaasBExpirationDatesAsync(
        List<SaasBSubscription> subscriptions,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Group by client, take the latest EndTimestamp per client
        var byClient = subscriptions
            .Where(s => s.EndTimestamp.HasValue)
            .GroupBy(s => s.ClientId)
            .ToDictionary(
                g => $"SAASB-{g.Key}",
                g => DateTimeOffset.FromUnixTimeSeconds(
                    g.OrderByDescending(s => s.EndTimestamp).First().EndTimestamp!.Value
                ).UtcDateTime);

        foreach (var (legacyId, expiresAt) in byClient)
        {
            var customer = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (customer is not null && customer.ExpirationDate != expiresAt)
            {
                customer.ExpirationDate = expiresAt;
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(ct);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static CustomerStatus MapSaasAStatus(string status) => status.ToLower() switch
    {
        "active" => CustomerStatus.Active,
        "lead" => CustomerStatus.Lead,
        "demo" or "trial" => CustomerStatus.Demo,
        "inactive" or "passive" => CustomerStatus.Demo, // legacy mapping → Demo
        "churned" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };

    // Segment is passed through as-is from SaaS as a free string (project-specific)

    private static CustomerStatus MapSaasBStatus(string state) => state.ToUpper() switch
    {
        "ACTIVE" => CustomerStatus.Active,
        "LEAD" => CustomerStatus.Lead,
        "DEMO" or "TRIAL" => CustomerStatus.Demo,
        "INACTIVE" or "PASSIVE" => CustomerStatus.Demo, // legacy mapping → Demo
        "CHURNED" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };


    private Guid GetProjectId(string configKey)
    {
        var value = _configuration[configKey];
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Resolves the project ID for a sync source.
    /// Always queries the DB with <c>IgnoreQueryFilters()</c> because background jobs
    /// run without an HTTP user context — the global tenant filter would otherwise
    /// block all project rows (IsSuperAdmin=false, ProjectIds=[]).
    /// Falls back to the first project with the matching API key when the config key is absent.
    /// </summary>
    private async Task<(Guid ProjectId, Project? Project)> ResolveProjectAsync(
        string configKey,
        Func<Project, bool> hasApiKey,
        CancellationToken ct)
    {
        // All project queries use a fresh scope + IgnoreQueryFilters to bypass the
        // tenant filter that blocks results when there is no active HTTP user context.
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var projectId = GetProjectId(configKey);

        if (projectId != Guid.Empty)
        {
            var project = await context.Projects
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == projectId, ct);

            if (project is not null)
            {
                _logger.LogDebug(
                    "Resolved project {ProjectId} ({Name}) via config key '{Key}'.",
                    project.Id, project.Name, configKey);
                return (projectId, project);
            }

            _logger.LogWarning(
                "Project {ProjectId} from config key '{Key}' not found in DB. Searching for fallback.",
                projectId, configKey);
        }

        // Config key missing or project not found — pick first project with this API key
        var allProjects = await context.Projects
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .ToListAsync(ct);

        var fallback = allProjects.FirstOrDefault(hasApiKey) ?? allProjects.FirstOrDefault();

        if (fallback is null)
        {
            _logger.LogWarning(
                "No projects found in DB. Skipping sync for config key '{Key}'.", configKey);
            return (Guid.Empty, null);
        }

        _logger.LogWarning(
            "Config key '{Key}' not set or invalid. Using fallback project {ProjectId} ({Name}).",
            configKey, fallback.Id, fallback.Name);

        return (fallback.Id, fallback);
    }
}
