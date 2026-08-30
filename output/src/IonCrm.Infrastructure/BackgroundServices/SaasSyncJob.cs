using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Sync.Commands.SyncEmsPayments;
using IonCrm.Application.Features.Sync.Commands.SyncRezervalContractInvoices;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using MediatR;
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
    private readonly IMediator _mediator;

    // Retry pipeline is built per-call so OnRetry can close over the in-memory SyncLog
    // instance and increment RetryCount.  Intentionally does NOT write to the DB during
    // retries — the final state is persisted once at the end of SyncWithRetryAsync, and
    // only when something meaningful happened (changes > 0 or a final failure).
    private ResiliencePipeline BuildRetryPipeline(SyncLog log) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    log.RetryCount++;
                    log.ErrorMessage = args.Outcome.Exception?.Message;

                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Sync retry #{Attempt} for {Source}/{EntityType}. Error: {Error}",
                        log.RetryCount, log.Source, log.EntityType,
                        args.Outcome.Exception?.Message);

                    return ValueTask.CompletedTask;
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
        ILogger<SaasSyncJob> logger,
        IMediator mediator)
    {
        _saasAClient = saasAClient;
        _saasBClient = saasBClient;
        _projectRepository = projectRepository;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _mediator = mediator;
    }

    /// <summary>
    /// Main sync job entry point — called by <see cref="SyncTimerService"/> or by the
    /// manual trigger endpoint.  Creates a DI scope per sync run to properly manage
    /// DbContext lifetime.
    /// </summary>
    /// <param name="emsPaymentWindowMinutes">
    /// EMS payment lookback window in minutes. The timer service passes a longer value
    /// (16h) on the first cycle after a long pause so overnight payments aren't missed.
    /// Default 20 keeps backward compatibility with the manual trigger endpoint.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(int emsPaymentWindowMinutes = 20, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SaaS sync job started at {Time:O}", DateTime.UtcNow);

        // EMS CUSTOMER sync is RETIRED (tenants moved to Liftdesk, CRM data migrated 2026-08-30)
        // and defaults OFF: the EMS API still answers and the global SaasA:ApiKey config kept the
        // customer sync alive even after the project's EmsApiKey was cleared — every cycle
        // re-inserted childless copies of the migrated companies. Set Sync:EmsEnabled=true only if
        // EMS customers ever need syncing again. (This gate is EMS-CUSTOMER-ONLY — see below.)
        var emsEnabled = string.Equals(_configuration["Sync:EmsEnabled"], "true", StringComparison.OrdinalIgnoreCase);
        if (emsEnabled)
            await SyncEmsCrmCustomersAsync(cancellationToken);
        else
            _logger.LogDebug("EMS CRM customer sync skipped — EMS is retired (Sync:EmsEnabled != true).");

        await SyncLiftdeskCustomersAsync(cancellationToken);
        await SyncRezervalCompaniesAsync(cancellationToken);

        // Payment → invoice-draft sync is NOT EMS-only: its handler builds one scan target per
        // configured credential (EmsApiKey AND/OR LiftdeskApiKey) per project. With EMS keys
        // cleared it scans Liftdesk targets exclusively, so it must run every cycle regardless of
        // Sync:EmsEnabled — gating it here is what silently stopped Liftdesk payment collection.
        await SyncEmsPaymentsAsync(emsPaymentWindowMinutes, cancellationToken);

        await SyncRezervalContractInvoicesAsync(cancellationToken);
        // await SyncSaasAAsync(cancellationToken);
        // await SyncSaasBAsync(cancellationToken);

        _logger.LogInformation("SaaS sync job completed at {Time:O}", DateTime.UtcNow);
    }

    // ── EMS CRM customers sync (new paginated endpoint) ───────────────────────

    /// <summary>
    /// Syncs customers from the EMS /api/v1/crm/customers endpoint.
    /// Always performs a full sync (no updatedSince delta filter) to ensure status is
    /// recomputed for ALL customers on every run — including those whose ExpirationDate
    /// has passed since the last sync without any data change.
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

        _logger.LogInformation("EMS CRM full sync: fetching all pages.");

        await SyncWithRetryAsync(
            source: SyncSource.SaasA,
            entityType: "CrmCustomer",
            projectId: projectId,
            action: async () =>
            {
                const int pageSize = 500;
                int page = 1, totalChanges = 0;
                // Track every Liftdesk companyId we see across all pages so the reconcile step
                // below can soft-delete rows that disappeared from Liftdesk.
                var seenLegacyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (true)
                {
                    var response = await _saasAClient.GetCrmCustomersPageAsync(
                        emsApiKey, page, pageSize, ct);

                    if (response.Data.Count == 0)
                        break;

                    foreach (var c in response.Data)
                        seenLegacyIds.Add(c.Id);

                    totalChanges += await UpsertEmsCrmCustomersAsync(response.Data, projectId, ct);

                    if (page >= response.TotalPages || response.TotalPages == 0)
                        break;

                    page++;
                }

                // Reconcile deletions — a customer removed on the Liftdesk side stays in the
                // response no more, so we soft-delete the local mirror. Only runs after a
                // successful full traversal (any page failure throws and skips this block).
                totalChanges += await ReconcileEmsCustomerDeletionsAsync(projectId, seenLegacyIds, ct);

                return totalChanges;
            });
    }

    private async Task<int> UpsertEmsCrmCustomersAsync(
        List<EmsCrmCustomer> customers,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int changeCount = 0;

        foreach (var src in customers)
        {
            // Canonical LegacyId = plain numeric EMS company ID (matches original DB migration).
            // Also check the old "SAASA-{id}" prefix format in case records were created by a
            // previous sync run before this normalization was applied.
            var legacyId         = src.Id;              // "3"
            var legacyIdPrefixed = $"SAASA-{src.Id}";  // "SAASA-3" (legacy)

            // All DateTimes from external APIs must be forced to UTC before storing in
            // PostgreSQL timestamp with time zone columns. System.Text.Json deserialises
            // dates without a timezone offset as Kind=Unspecified; Npgsql rejects these.
            var expDate    = src.ExpirationDate.HasValue
                ? DateTime.SpecifyKind(src.ExpirationDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;
            var createdAt  = DateTime.SpecifyKind(src.CreatedAt, DateTimeKind.Utc);

            var newStatus = ComputeStatusFromExpiration(expDate, createdAt);

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => c.LegacyId == legacyId || c.LegacyId == legacyIdPrefixed)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // If the user soft-deleted this customer, respect that decision — don't
                // resurrect it on the next sync cycle. Skip silently.
                if (existing.IsDeleted) continue;

                // Normalize to canonical format if it was stored with old prefix
                bool changed = false;
                if (existing.LegacyId == legacyIdPrefixed) { existing.LegacyId = legacyId; changed = true; }

                if (existing.CompanyName    != src.Name)    { existing.CompanyName    = src.Name;    changed = true; }
                if (existing.Email          != src.Email)   { existing.Email          = src.Email;   changed = true; }
                if (existing.Phone          != src.Phone)   { existing.Phone          = src.Phone;   changed = true; }
                if (existing.Address        != src.Address) { existing.Address        = src.Address; changed = true; }
                if (existing.TaxNumber      != src.TaxNumber) { existing.TaxNumber   = src.TaxNumber; changed = true; }
                if (existing.Segment        != src.Segment) { existing.Segment        = src.Segment; changed = true; }
                if (existing.ExpirationDate != expDate)     { existing.ExpirationDate = expDate;     changed = true; }
                if (existing.Status         != newStatus)   { existing.Status         = newStatus;   changed = true; }
                // Backfill CreatedAt from EMS if it was set to the sync date instead of the original
                if (existing.CreatedAt != createdAt)        { existing.CreatedAt      = createdAt;   changed = true; }
                if (changed)
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                    changeCount++;
                }
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id             = Guid.NewGuid(),
                    ProjectId      = projectId,
                    LegacyId       = legacyId,
                    CompanyName    = src.Name,
                    Email          = src.Email,
                    Phone          = src.Phone,
                    Address        = src.Address,
                    TaxNumber      = src.TaxNumber,
                    Segment        = src.Segment,
                    ExpirationDate = expDate,
                    Status         = newStatus,
                    CreatedAt      = createdAt,
                    UpdatedAt      = DateTime.UtcNow
                });
                changeCount++;
            }
        }

        // SaveChanges per page-batch — if it fails log the error and continue
        // rather than losing the entire batch due to one bad record.
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS CRM upsert SaveChanges failed for a batch of {Count} records. Inner: {Inner}",
                customers.Count, ex.InnerException?.Message ?? ex.Message);
            throw; // let SyncWithRetryAsync handle retries / Failed status
        }

        return changeCount;
    }

    /// <summary>
    /// Soft-deletes local Customer rows that used to come from Liftdesk (numeric LegacyId or
    /// <c>SAASA-*</c> prefix) but were absent from the full sync response — i.e. deleted on the
    /// Liftdesk side.  Runs only when the response contained at least one record so an empty API
    /// result never wipes the mirror, and refuses to run when the missing set exceeds half of the
    /// local rows (probably a truncated response from a Liftdesk glitch, not real deletions).
    /// Returns the number of rows soft-deleted (0 when the safeguards trip).
    /// </summary>
    private async Task<int> ReconcileEmsCustomerDeletionsAsync(
        Guid projectId,
        HashSet<string> seenLegacyIds,
        CancellationToken ct)
    {
        if (seenLegacyIds.Count == 0)
        {
            // Empty response — refuse to touch the mirror. Either the project has no customers
            // on Liftdesk yet, or the API glitched and we cannot tell the difference.
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Load the *active* Liftdesk-sourced customers in this project. Post-fetch filter in-code
        // because the "starts with digit OR SAASA-" predicate isn't clean in EF LINQ.
        // Background jobs run with no HTTP user context, so we must bypass the tenant filter.
        var candidates = await context.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId && !c.IsDeleted && c.LegacyId != null)
            .Select(c => new { c.Id, c.LegacyId, c.CompanyName })
            .ToListAsync(ct);

        var localEms = candidates
            .Where(c => IsLiftdeskLegacyId(c.LegacyId!))
            .ToList();

        if (localEms.Count == 0)
            return 0;

        // Which locals aren't in the current API result? Normalize both to bare-numeric form so
        // a legacy "SAASA-3" row matches a fresh "3" response entry.
        var toDelete = localEms
            .Where(c => !seenLegacyIds.Contains(NormalizeEmsLegacyId(c.LegacyId!)))
            .ToList();

        if (toDelete.Count == 0)
            return 0;

        // Safety net: if we would soft-delete more than half of the local Liftdesk customers in
        // one cycle, assume the API returned a truncated result set and skip. Legit mass deletions
        // can either be applied over multiple cycles (once the API returns clean data) or done
        // manually. Only enforce this above a small floor so a tiny project can still evict its
        // 1-of-3 deletion cleanly.
        if (localEms.Count > 10 && toDelete.Count > localEms.Count / 2)
        {
            _logger.LogWarning(
                "EMS reconcile: would soft-delete {ToDelete}/{Local} customers — refusing " +
                "(suspicious API truncation). Fetched={Fetched}.",
                toDelete.Count, localEms.Count, seenLegacyIds.Count);
            return 0;
        }

        var now = DateTime.UtcNow;
        int softDeleted = 0;
        foreach (var c in toDelete)
        {
            try
            {
                // Raw SQL keeps this a single indexed UPDATE with no change-tracker overhead and
                // sidesteps the tenant query filter which would otherwise hide the row.
                await context.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Customers"" SET ""IsDeleted"" = true, ""UpdatedAt"" = {0} WHERE ""Id"" = {1}",
                    now, c.Id);
                softDeleted++;

                _logger.LogInformation(
                    "EMS reconcile: soft-deleted customer {CustomerId} legacyId={LegacyId} ({CompanyName}) — no longer in Liftdesk.",
                    c.Id, c.LegacyId, c.CompanyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "EMS reconcile: failed to soft-delete customer {CustomerId} legacyId={LegacyId}.",
                    c.Id, c.LegacyId);
            }
        }

        return softDeleted;
    }

    /// <summary>
    /// Returns true when <paramref name="legacyId"/> looks like a Liftdesk (EMS) customer key —
    /// either a bare numeric id ("3") or the <c>SAASA-{id}</c> prefix used by older sync runs.
    /// Excludes Rezerval (<c>SAASB-*</c>/<c>REZV-*</c>) and PotentialCustomer (<c>PC-*</c>) rows.
    /// </summary>
    private static bool IsLiftdeskLegacyId(string legacyId)
    {
        if (string.IsNullOrEmpty(legacyId)) return false;
        if (legacyId.StartsWith("PC-",    StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("REZV-",  StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)) return false;
        if (legacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)) return true;
        return char.IsDigit(legacyId[0]);
    }

    /// <summary>Strips the legacy <c>SAASA-</c> prefix so a stored id matches the Liftdesk response id.</summary>
    private static string NormalizeEmsLegacyId(string legacyId) =>
        legacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)
            ? legacyId["SAASA-".Length..]
            : legacyId;

    /// <summary>
    /// Computes customer status based on ExpirationDate rules:
    /// <list type="bullet">
    ///   <item>Demo:    CreatedAt+40d &gt; ExpirationDate AND today &lt; ExpirationDate (short trial, not yet expired)</item>
    ///   <item>Passive: CreatedAt+40d &gt; ExpirationDate AND ExpirationDate &lt; today (short trial, expired)</item>
    ///   <item>Churn:   CreatedAt+40d &lt; ExpirationDate AND ExpirationDate &lt; today (real customer, expired)</item>
    ///   <item>Active:  CreatedAt+40d &lt; ExpirationDate AND today &lt; ExpirationDate (real customer, not yet expired)</item>
    ///   <item>Lead:    no ExpirationDate set</item>
    /// </list>
    /// Boundary (today == ExpirationDate) is treated as expired (strict inequality: bugün &lt; ExpirationDate).
    /// </summary>
    private static CustomerStatus ComputeStatusFromExpiration(DateTime? expirationDate, DateTime createdOn)
    {
        if (!expirationDate.HasValue)
            return CustomerStatus.Lead;

        var today   = DateTime.UtcNow.Date;
        var exp     = expirationDate.Value.Date;
        var created = createdOn.Date;
        var createdPlus40 = created.AddDays(40);

        // Short trial: CreatedAt + 40 days > ExpirationDate
        bool isShortTrial = createdPlus40 > exp;

        // Strict inequality: today < exp means "not yet expired" (on expiration day itself → expired)
        bool notExpired = today < exp;

        if (isShortTrial)
        {
            // (1) Demo:    CreatedAt+40d > ExpirationDate AND today < ExpirationDate
            // (2) Passive: CreatedAt+40d > ExpirationDate AND ExpirationDate < today (or == today)
            return notExpired
                ? CustomerStatus.Demo
                : CustomerStatus.Passive;
        }
        else
        {
            // (4) Active:  CreatedAt+40d < ExpirationDate AND today < ExpirationDate
            // (3) Churn:   CreatedAt+40d < ExpirationDate AND ExpirationDate < today (or == today)
            return notExpired
                ? CustomerStatus.Active
                : CustomerStatus.Churned;
        }
    }

    // ── Liftdesk customers sync (same EMS-style /api/v1/crm/customers endpoint) ──

    /// <summary>
    /// Syncs customers from a Liftdesk project. Liftdesk exposes the identical SaaS surface as EMS,
    /// so it reuses <see cref="ISaasAClient"/> with the project's Liftdesk base URL + key. Customers
    /// are stored under the "LIFT-{id}" LegacyId prefix so they never collide with EMS customers that
    /// share the same numeric id (the upsert matches on LegacyId across all tenants).
    /// </summary>
    private async Task SyncLiftdeskCustomersAsync(CancellationToken ct)
    {
        // Require BOTH key and base URL: unlike EMS (which has a global SaasA:BaseUrl fallback),
        // Liftdesk has no default host, so a key without a base URL would misfire against the EMS host.
        var (projectId, project) = await ResolveProjectAsync(
            "Liftdesk:ProjectId",
            p => !string.IsNullOrEmpty(p.LiftdeskApiKey) && !string.IsNullOrEmpty(p.LiftdeskBaseUrl),
            ct);

        if (projectId == Guid.Empty
            || string.IsNullOrWhiteSpace(project?.LiftdeskApiKey)
            || string.IsNullOrWhiteSpace(project.LiftdeskBaseUrl))
        {
            _logger.LogInformation("No project with a complete Liftdesk configuration (API key + base URL). Skipping Liftdesk sync.");
            return;
        }

        var apiKey  = project.LiftdeskApiKey;
        var baseUrl = project.LiftdeskBaseUrl;

        _logger.LogInformation("Liftdesk full sync: fetching all pages for project {ProjectId}.", projectId);

        await SyncWithRetryAsync(
            source: SyncSource.Liftdesk,
            entityType: "LiftdeskCustomer",
            projectId: projectId,
            action: async () =>
            {
                const int pageSize = 500;
                int page = 1, totalChanges = 0;
                // Track every Liftdesk company id we see across all pages so the reconcile
                // step below can soft-delete rows that disappeared from the source.
                var seenSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (true)
                {
                    var response = await _saasAClient.GetCrmCustomersPageAsync(
                        apiKey, page, pageSize, ct, baseUrl);

                    if (response.Data.Count == 0)
                        break;

                    foreach (var c in response.Data)
                        seenSourceIds.Add(c.Id);

                    totalChanges += await UpsertLiftdeskCustomersAsync(response.Data, projectId, ct);

                    if (page >= response.TotalPages || response.TotalPages == 0)
                        break;

                    page++;
                }

                // Reconcile deletions — customers absent from a completed full traversal are
                // treated as deleted on the Liftdesk side and soft-deleted locally.
                totalChanges += await ReconcileLiftdeskCustomerDeletionsAsync(projectId, seenSourceIds, ct);

                return totalChanges;
            });
    }

    /// <summary>
    /// Soft-deletes local Customer rows whose LegacyId is <c>LIFT-{id}</c> for this project when
    /// <paramref name="seenSourceIds"/> does not contain <c>{id}</c> — i.e. the Liftdesk source no
    /// longer returns that customer. Same safeguards as the EMS reconcile: empty response is a
    /// no-op, and the operation aborts when the missing set exceeds half of the local rows.
    /// </summary>
    private async Task<int> ReconcileLiftdeskCustomerDeletionsAsync(
        Guid projectId,
        HashSet<string> seenSourceIds,
        CancellationToken ct)
    {
        if (seenSourceIds.Count == 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locals = await context.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId
                     && !c.IsDeleted
                     && c.LegacyId != null
                     && c.LegacyId.StartsWith("LIFT-"))
            .Select(c => new { c.Id, c.LegacyId, c.CompanyName })
            .ToListAsync(ct);

        if (locals.Count == 0)
            return 0;

        var toDelete = locals
            .Where(c => !seenSourceIds.Contains(c.LegacyId!["LIFT-".Length..]))
            .ToList();

        if (toDelete.Count == 0)
            return 0;

        if (locals.Count > 10 && toDelete.Count > locals.Count / 2)
        {
            _logger.LogWarning(
                "Liftdesk reconcile: would soft-delete {ToDelete}/{Local} customers — refusing " +
                "(suspicious API truncation). Fetched={Fetched}.",
                toDelete.Count, locals.Count, seenSourceIds.Count);
            return 0;
        }

        var now = DateTime.UtcNow;
        int softDeleted = 0;
        foreach (var c in toDelete)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Customers"" SET ""IsDeleted"" = true, ""UpdatedAt"" = {0} WHERE ""Id"" = {1}",
                    now, c.Id);
                softDeleted++;

                _logger.LogInformation(
                    "Liftdesk reconcile: soft-deleted customer {CustomerId} legacyId={LegacyId} ({CompanyName}) — no longer in Liftdesk.",
                    c.Id, c.LegacyId, c.CompanyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Liftdesk reconcile: failed to soft-delete customer {CustomerId} legacyId={LegacyId}.",
                    c.Id, c.LegacyId);
            }
        }

        return softDeleted;
    }

    private async Task<int> UpsertLiftdeskCustomersAsync(
        List<EmsCrmCustomer> customers,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int changeCount = 0;

        foreach (var src in customers)
        {
            // "LIFT-{id}" keeps Liftdesk customers distinct from EMS ("3"/"SAASA-3") in the
            // cross-tenant LegacyId upsert lookup.
            var legacyId = $"LIFT-{src.Id}";

            var expDate = src.ExpirationDate.HasValue
                ? DateTime.SpecifyKind(src.ExpirationDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;
            var createdAt = DateTime.SpecifyKind(src.CreatedAt, DateTimeKind.Utc);

            var newStatus = ComputeStatusFromExpiration(expDate, createdAt);

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                if (existing.IsDeleted) continue;

                bool changed = false;
                if (existing.CompanyName    != src.Name)      { existing.CompanyName    = src.Name;      changed = true; }
                if (existing.Email          != src.Email)     { existing.Email          = src.Email;     changed = true; }
                if (existing.Phone          != src.Phone)     { existing.Phone          = src.Phone;     changed = true; }
                if (existing.Address        != src.Address)   { existing.Address        = src.Address;   changed = true; }
                if (existing.TaxNumber      != src.TaxNumber) { existing.TaxNumber      = src.TaxNumber; changed = true; }
                if (existing.Segment        != src.Segment)   { existing.Segment        = src.Segment;   changed = true; }
                if (existing.ExpirationDate != expDate)       { existing.ExpirationDate = expDate;       changed = true; }
                if (existing.Status         != newStatus)     { existing.Status         = newStatus;     changed = true; }
                if (existing.CreatedAt      != createdAt)     { existing.CreatedAt      = createdAt;     changed = true; }
                if (changed)
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                    changeCount++;
                }
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id             = Guid.NewGuid(),
                    ProjectId      = projectId,
                    LegacyId       = legacyId,
                    CompanyName    = src.Name,
                    Email          = src.Email,
                    Phone          = src.Phone,
                    Address        = src.Address,
                    TaxNumber      = src.TaxNumber,
                    Segment        = src.Segment,
                    ExpirationDate = expDate,
                    Status         = newStatus,
                    CreatedAt      = createdAt,
                    UpdatedAt      = DateTime.UtcNow
                });
                changeCount++;
            }
        }

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk upsert SaveChanges failed for a batch of {Count} records. Inner: {Inner}",
                customers.Count, ex.InnerException?.Message ?? ex.Message);
            throw;
        }

        return changeCount;
    }

    // ── Rezerval CRM company sync ─────────────────────────────────────────────

    /// <summary>
    /// Syncs companies from the Rezerval CRM API (https://rezback.rezerval.com/v1/Crm/CompanyList).
    /// Full sync on every run — status is recomputed from ExperationDate + CreatedOn using the same
    /// 40-day threshold rule as EMS. LegacyId format: "REZV-{id}".
    /// Deleted companies (IsDeleted=true) are skipped.
    /// </summary>
    private async Task SyncRezervalCompaniesAsync(CancellationToken ct)
    {
        var (projectId, project) = await ResolveProjectAsync(
            "SaasB:ProjectId", p => !string.IsNullOrEmpty(p.RezervAlApiKey), ct);

        if (projectId == Guid.Empty)
        {
            _logger.LogWarning("No project found for Rezerval CRM sync. Skipping.");
            return;
        }

        var rezervAlApiKey = project?.RezervAlApiKey;

        _logger.LogInformation(
            "Rezerval CRM full sync: fetching all companies for project {ProjectId}. ApiKey configured: {HasKey}",
            projectId, rezervAlApiKey is not null);

        await SyncWithRetryAsync(
            source: SyncSource.SaasB,
            entityType: "RezervalCompany",
            projectId: projectId,
            action: async () =>
            {
                var companies = await _saasBClient.GetRezervalCompaniesAsync(rezervAlApiKey, ct);
                int totalChanges = await UpsertRezervalCompaniesAsync(companies, projectId, ct);

                // Reconcile deletions — companies absent from the response are treated as deleted
                // on the Rezerval side and soft-deleted locally. The upsert step handles the case
                // where the API returns the record with IsDeleted=true (also soft-deleted below).
                // seenSourceIds includes IsDeleted rows too: if Rezerval later restores one we won't
                // reappearing-then-mass-deleting it.
                var seenSourceIds = new HashSet<int>(companies.Select(c => c.Id));
                totalChanges += await ReconcileRezervalCompanyDeletionsAsync(projectId, seenSourceIds, ct);

                return totalChanges;
            });
    }

    private async Task<int> UpsertRezervalCompaniesAsync(
        List<RezervalCompany> companies,
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int changeCount = 0;

        foreach (var src in companies)
        {
            var legacyId = $"REZV-{src.Id}";

            // Rezerval returns tombstones with IsDeleted=true. If we have a matching local row
            // still marked live, soft-delete it (parallel to the reconcile pass, which only
            // catches companies that vanish from the response entirely).
            if (src.IsDeleted)
            {
                var tombstone = await context.Customers
                    .IgnoreQueryFilters()
                    .Where(c => c.LegacyId == legacyId && !c.IsDeleted)
                    .Select(c => new { c.Id, c.CompanyName })
                    .FirstOrDefaultAsync(ct);

                if (tombstone is not null)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        @"UPDATE ""Customers"" SET ""IsDeleted"" = true, ""UpdatedAt"" = {0} WHERE ""Id"" = {1}",
                        DateTime.UtcNow, tombstone.Id);
                    changeCount++;

                    _logger.LogInformation(
                        "Rezerval sync: soft-deleted customer {CustomerId} legacyId={LegacyId} ({CompanyName}) — marked deleted on Rezerval.",
                        tombstone.Id, legacyId, tombstone.CompanyName);
                }
                continue;
            }

            // Force UTC — Rezerval API returns datetimes without timezone offset;
            // System.Text.Json deserialises them as Kind=Unspecified which Npgsql rejects.
            var expDate   = DateTime.SpecifyKind(src.ExperationDate, DateTimeKind.Utc);
            var createdOn = DateTime.SpecifyKind(src.CreatedOn,      DateTimeKind.Utc);

            var newStatus = ComputeStatusFromExpiration(expDate, createdOn);

            var existing = await context.Customers
                .IgnoreQueryFilters()
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                if (existing.IsDeleted) continue;

                bool changed = false;
                if (existing.CompanyName    != src.Name)      { existing.CompanyName    = src.Name;      changed = true; }
                if (existing.Email          != src.Email)     { existing.Email          = src.Email;     changed = true; }
                if (existing.Phone          != src.Phone)     { existing.Phone          = src.Phone;     changed = true; }
                if (existing.Address        != src.Address)   { existing.Address        = src.Address;   changed = true; }
                if (existing.TaxNumber      != src.TaxNumber) { existing.TaxNumber      = src.TaxNumber; changed = true; }
                if (existing.TaxUnit        != src.TaxUnit)   { existing.TaxUnit        = src.TaxUnit;   changed = true; }
                if (existing.ExpirationDate != expDate)       { existing.ExpirationDate = expDate;       changed = true; }
                if (existing.Status         != newStatus)     { existing.Status         = newStatus;     changed = true; }
                if (existing.LogoUrl        != src.Logo)      { existing.LogoUrl        = src.Logo;      changed = true; }
                if (existing.CreatedAt      != createdOn)     { existing.CreatedAt      = createdOn;     changed = true; }
                // ContactName and Segment are CRM-only fields — not in CompanyList API.
                if (changed)
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                    changeCount++;
                }
            }
            else
            {
                context.Customers.Add(new Customer
                {
                    Id             = Guid.NewGuid(),
                    ProjectId      = projectId,
                    LegacyId       = legacyId,
                    CompanyName    = src.Name,
                    Email          = src.Email,
                    Phone          = src.Phone,
                    Address        = src.Address,
                    TaxNumber      = src.TaxNumber,
                    TaxUnit        = src.TaxUnit,
                    LogoUrl        = src.Logo,
                    ExpirationDate = expDate,
                    Status         = newStatus,
                    CreatedAt      = createdOn,
                    UpdatedAt      = DateTime.UtcNow
                });
                changeCount++;
            }
        }

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval CRM upsert SaveChanges failed. Inner: {Inner}",
                ex.InnerException?.Message ?? ex.Message);
            throw;
        }

        return changeCount;
    }

    /// <summary>
    /// Soft-deletes local Customer rows whose LegacyId is <c>REZV-{id}</c> for this project when
    /// <paramref name="seenSourceIds"/> does not contain <c>{id}</c> — i.e. the Rezerval CompanyList
    /// response omitted that company entirely (as opposed to returning it with IsDeleted=true, which
    /// the upsert step handles inline). Same safeguards as the EMS/Liftdesk reconcile: empty
    /// response is a no-op, and the operation aborts when the missing set exceeds half of the
    /// local rows.
    /// </summary>
    private async Task<int> ReconcileRezervalCompanyDeletionsAsync(
        Guid projectId,
        HashSet<int> seenSourceIds,
        CancellationToken ct)
    {
        if (seenSourceIds.Count == 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locals = await context.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId
                     && !c.IsDeleted
                     && c.LegacyId != null
                     && c.LegacyId.StartsWith("REZV-"))
            .Select(c => new { c.Id, c.LegacyId, c.CompanyName })
            .ToListAsync(ct);

        if (locals.Count == 0)
            return 0;

        var toDelete = locals
            .Where(c =>
            {
                // Rezerval company ids are integers; strip the prefix and parse.
                var raw = c.LegacyId!["REZV-".Length..];
                return int.TryParse(raw, out var id) && !seenSourceIds.Contains(id);
            })
            .ToList();

        if (toDelete.Count == 0)
            return 0;

        if (locals.Count > 10 && toDelete.Count > locals.Count / 2)
        {
            _logger.LogWarning(
                "Rezerval reconcile: would soft-delete {ToDelete}/{Local} customers — refusing " +
                "(suspicious API truncation). Fetched={Fetched}.",
                toDelete.Count, locals.Count, seenSourceIds.Count);
            return 0;
        }

        var now = DateTime.UtcNow;
        int softDeleted = 0;
        foreach (var c in toDelete)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Customers"" SET ""IsDeleted"" = true, ""UpdatedAt"" = {0} WHERE ""Id"" = {1}",
                    now, c.Id);
                softDeleted++;

                _logger.LogInformation(
                    "Rezerval reconcile: soft-deleted customer {CustomerId} legacyId={LegacyId} ({CompanyName}) — no longer in Rezerval.",
                    c.Id, c.LegacyId, c.CompanyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Rezerval reconcile: failed to soft-delete customer {CustomerId} legacyId={LegacyId}.",
                    c.Id, c.LegacyId);
            }
        }

        return softDeleted;
    }

    // ── EMS payment → invoice draft sync ─────────────────────────────────────

    /// <summary>
    /// Fetches EMS payments from the configured lookback window and creates invoice
    /// drafts for any payment not yet recorded. Uses MediatR to reuse the same logic
    /// as the manual <c>POST /api/v1/sync/ems-payments</c> endpoint.
    /// </summary>
    /// <param name="windowMinutes">How far back to look for payments (minutes).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task SyncEmsPaymentsAsync(int windowMinutes, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new SyncEmsPaymentsCommand(WindowMinutes: windowMinutes), ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("EMS payment sync returned failure: {Errors}", result.Errors);
                return;
            }

            // Only log to console when something meaningful happened, to keep idle cycles quiet.
            var summary = result.Value!;
            if (summary.InvoicesCreated > 0 || summary.Errors.Count > 0)
            {
                _logger.LogInformation(
                    "EMS payment sync: projects={Projects} payments={Payments} created={Created} skipped={Skipped} errors={Errors}.",
                    summary.ProjectsScanned, summary.PaymentsFetched,
                    summary.InvoicesCreated, summary.Skipped, summary.Errors.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EMS payment sync threw an unhandled exception.");
        }
    }

    // ── Rezerval contract → monthly EFT invoice sync ─────────────────────────

    /// <summary>
    /// Generates monthly draft invoices for active EFT/Wire customer contracts whose
    /// <c>NextInvoiceDate</c> is on or before today.  Idempotent: each (contract, month) pair
    /// is uniquely keyed in <c>Invoice.EmsPaymentId</c> so re-running the job is safe.
    /// </summary>
    private async Task SyncRezervalContractInvoicesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new SyncRezervalContractInvoicesCommand(), ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Rezerval contract invoice sync returned failure: {Errors}", result.Errors);
                return;
            }

            // Only log when something meaningful happened.
            var summary = result.Value!;
            if (summary.InvoicesCreated > 0
                || summary.ContractsCompleted > 0
                || summary.Errors.Count > 0)
            {
                _logger.LogInformation(
                    "Rezerval contract invoice sync: scanned={Scanned} created={Created} skipped={Skipped} completed={Completed} errors={Errors}.",
                    summary.ContractsScanned, summary.InvoicesCreated,
                    summary.Skipped, summary.ContractsCompleted, summary.Errors.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rezerval contract invoice sync threw an unhandled exception.");
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
    /// The <paramref name="action"/> must return the number of records that were actually
    /// inserted or updated (NOT the total fetched). A SyncLog row is persisted to the DB
    /// only when at least one record changed OR when the final attempt threw — quiet
    /// "nothing-to-do" cycles leave no audit row, which keeps the SyncLogs view focused
    /// on meaningful events and avoids needless Neon compute charges.
    /// </summary>
    private async Task SyncWithRetryAsync(
        SyncSource source,
        string entityType,
        Guid projectId,
        Func<Task<int>> action)
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Source = source,
            Direction = SyncDirection.Inbound,
            EntityType = entityType,
            Status = SyncStatus.Pending
        };

        // Retry pipeline mutates `log` in memory only — no DB writes during retries.
        var pipeline = BuildRetryPipeline(log);

        try
        {
            var changeCount = await pipeline.ExecuteAsync<int>(
                async _ => await action(),
                CancellationToken.None);

            // Quiet success: nothing changed, no error — do not write a SyncLog row.
            if (changeCount <= 0)
                return;

            log.Status   = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var syncLogRepo = scope.ServiceProvider.GetRequiredService<ISyncLogRepository>();
            await syncLogRepo.AddAsync(log);

            _logger.LogInformation(
                "{Source} {EntityType} sync succeeded. New/changed records: {Count}",
                source, entityType, changeCount);
        }
        catch (Exception ex)
        {
            log.Status       = SyncStatus.Failed;
            log.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            log.SyncedAt     = DateTime.UtcNow;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncLogRepo = scope.ServiceProvider.GetRequiredService<ISyncLogRepository>();
                await syncLogRepo.AddAsync(log);
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx,
                    "Failed to persist Failed SyncLog for {Source}/{EntityType}.",
                    source, entityType);
            }

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
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                if (existing.IsDeleted) continue;

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
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                if (existing.IsDeleted) continue;

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
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (customer is not null && !customer.IsDeleted && customer.ExpirationDate != expiresAt)
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
                .Where(c => c.LegacyId == legacyId)
                .FirstOrDefaultAsync(ct);

            if (customer is not null && !customer.IsDeleted && customer.ExpirationDate != expiresAt)
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

        // Prefer a project that has the API key configured; fall back to the first project
        // if none do (the global SaasA:ApiKey / SaasB:ApiKey from config will be used instead).
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
