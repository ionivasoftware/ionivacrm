using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for SaaS A external system.
/// SaaS A uses standard REST endpoints under /api/v1/ with Bearer token authentication.
///
/// Retry policy (Polly v8 ResiliencePipeline):
///   • 3 retries after the initial attempt (4 total)
///   • Exponential backoff: 2 s → 4 s → 8 s (± jitter)
///   • Handles <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>
///   • Each retry attempt is logged via the injected <see cref="ILogger{T}"/>
/// </summary>
public sealed class SaasAClient : ISaasAClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SaasAClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="SaasAClient"/>.
    /// The <paramref name="httpClient"/> is pre-configured by DI (base address + Bearer auth header).
    /// </summary>
    public SaasAClient(HttpClient httpClient, ILogger<SaasAClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Build a per-instance Polly v8 pipeline so the OnRetry delegate can
        // capture the logger for structured retry logging.
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "SaaS A HTTP retry #{Attempt} in {Delay:0.##}s. Error: {Error}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<SaasACustomersResponse> GetCustomersAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching customers.");
        return await _retryPipeline.ExecuteAsync<SaasACustomersResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/customers");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasACustomersResponse>(JsonOpts, ct);
            return result ?? new SaasACustomersResponse(new List<SaasACustomer>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmsCrmCustomersResponse> GetCrmCustomersPageAsync(
        string? apiKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching CRM customers page={Page} pageSize={PageSize} (full sync)",
            page, pageSize);

        return await _retryPipeline.ExecuteAsync<EmsCrmCustomersResponse>(async ct =>
        {
            var url = $"api/v1/crm/customers?page={page}&pageSize={pageSize}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmsCrmCustomersResponse>(JsonOpts, ct);
            return result ?? new EmsCrmCustomersResponse(new List<EmsCrmCustomer>(), 0, page, pageSize, 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasASubscriptionsResponse> GetSubscriptionsAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching subscriptions.");
        return await _retryPipeline.ExecuteAsync<SaasASubscriptionsResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/subscriptions");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasASubscriptionsResponse>(JsonOpts, ct);
            return result ?? new SaasASubscriptionsResponse(new List<SaasASubscription>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasAOrdersResponse> GetOrdersAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching orders.");
        return await _retryPipeline.ExecuteAsync<SaasAOrdersResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/orders");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasAOrdersResponse>(JsonOpts, ct);
            return result ?? new SaasAOrdersResponse(new List<SaasAOrder>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyCallbackAsync(SaasACallbackPayload payload, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "SaaS A: posting callback. EventType={EventType} EntityType={EntityType} EntityId={EntityId}",
            payload.EventType, payload.EntityType, payload.EntityId);

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/crm-callbacks");
            ApplyAuth(request, apiKey);
            request.Content = JsonContent.Create(payload, options: JsonOpts);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmsExtendExpirationResponse> ExtendExpirationAsync(
        string? apiKey,
        int emsCompanyId,
        string durationType,
        int amount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: extending expiration for company {CompanyId}. {DurationType}={Amount}",
            emsCompanyId, durationType, amount);

        return await _retryPipeline.ExecuteAsync<EmsExtendExpirationResponse>(async ct =>
        {
            var url = $"api/v1/crm/companies/{emsCompanyId}/extend-expiration";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyAuth(request, apiKey);
            request.Content = JsonContent.Create(
                new { durationType, amount },
                options: JsonOpts);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmsExtendExpirationResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty response from EMS extend-expiration.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmsAddSmsResponse> AddSmsAsync(
        string? apiKey,
        int emsCompanyId,
        int count,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: adding {Count} SMS credits for company {CompanyId}.", count, emsCompanyId);

        return await _retryPipeline.ExecuteAsync<EmsAddSmsResponse>(async ct =>
        {
            var url = $"api/v1/crm/companies/{emsCompanyId}/add-sms";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyAuth(request, apiKey);
            request.Content = JsonContent.Create(new { count }, options: JsonOpts);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmsAddSmsResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty response from EMS add-sms.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<EmsCompanyUser>> GetCompanyUsersAsync(
        string? apiKey,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching users for company {CompanyId}.", companyId);

        return await _retryPipeline.ExecuteAsync<List<EmsCompanyUser>>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/crm/companies/{companyId}/users");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmsCompanyUsersResponse>(JsonOpts, ct);
            return result?.Data ?? new List<EmsCompanyUser>();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmsCompanySummaryResponse> GetCompanySummaryAsync(
        string? apiKey,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS A: fetching summary for company {CompanyId}.", companyId);

        return await _retryPipeline.ExecuteAsync<EmsCompanySummaryResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/crm/companies/{companyId}/summary");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmsCompanySummaryResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty response from EMS company summary.");
        }, cancellationToken);
    }

    /// <summary>
    /// Overrides the default Authorization header with a project-specific Bearer token when provided.
    /// Falls back to the header pre-configured in DI (appsettings SaasA:ApiKey) when apiKey is null/empty.
    /// </summary>
    private static void ApplyAuth(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}
