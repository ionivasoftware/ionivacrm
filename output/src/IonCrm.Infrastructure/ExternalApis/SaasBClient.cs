using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for SaaS B external system.
/// SaaS B uses different endpoint paths (/customers/list, /subscriptions/all, /orders/all)
/// and X-Api-Key header authentication (configured at registration time in DI).
///
/// Retry policy (Polly v8 ResiliencePipeline):
///   • 3 retries after the initial attempt (4 total)
///   • Exponential backoff: 2 s → 4 s → 8 s (± jitter)
///   • Handles <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>
///   • Each retry attempt is logged via the injected <see cref="ILogger{T}"/>
/// </summary>
public sealed class SaasBClient : ISaasBClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SaasBClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="SaasBClient"/>.
    /// The <paramref name="httpClient"/> is pre-configured by DI (base address + X-Api-Key header).
    /// </summary>
    public SaasBClient(HttpClient httpClient, ILogger<SaasBClient> logger)
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
                        "SaaS B HTTP retry #{Attempt} in {Delay:0.##}s. Error: {Error}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<SaasBCustomersResponse> GetCustomersAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching customers.");
        return await _retryPipeline.ExecuteAsync<SaasBCustomersResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "customers/list");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBCustomersResponse>(JsonOpts, ct);
            return result ?? new SaasBCustomersResponse(new List<SaasBCustomer>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasBSubscriptionsResponse> GetSubscriptionsAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching subscriptions.");
        return await _retryPipeline.ExecuteAsync<SaasBSubscriptionsResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "subscriptions/all");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBSubscriptionsResponse>(JsonOpts, ct);
            return result ?? new SaasBSubscriptionsResponse(new List<SaasBSubscription>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasBOrdersResponse> GetOrdersAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching orders.");
        return await _retryPipeline.ExecuteAsync<SaasBOrdersResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "orders/all");
            ApplyAuth(request, apiKey);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBOrdersResponse>(JsonOpts, ct);
            return result ?? new SaasBOrdersResponse(new List<SaasBOrder>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyCallbackAsync(SaasBCallbackPayload payload, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "SaaS B: posting callback. Event={Event} Type={Type} Id={Id}",
            payload.Event, payload.Type, payload.Id);

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "webhooks/crm");
            ApplyAuth(request, apiKey);
            request.Content = JsonContent.Create(payload, options: JsonOpts);
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<RezervalCompany>> GetRezervalCompaniesAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rezerval CRM: fetching company list.");
        return await _retryPipeline.ExecuteAsync<List<RezervalCompany>>(async ct =>
        {
            // Absolute URL overrides the SaasBClient base address — Rezerval CRM uses
            // a different host (rezback.rezerval.com) and Bearer token auth instead of X-Api-Key.
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://rezback.rezerval.com/v1/Crm/CompanyList");

            ApplyBearerAuth(request, apiKey);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<List<RezervalCompany>>(JsonOpts, ct);
            return result ?? new List<RezervalCompany>();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalCreateCompanyResponse> CreateRezervalCompanyAsync(
        RezervalCompanyFormData data,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rezerval CRM: creating company {Name}.", data.Name);
        return await _retryPipeline.ExecuteAsync<RezervalCreateCompanyResponse>(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://rezback.rezerval.com/v1/Crm/Company");

            ApplyBearerAuth(request, apiKey);
            request.Content = BuildMultipartContent(data);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RezervalCreateCompanyResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Rezerval create company returned empty response.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateRezervalCompanyAsync(
        int companyId,
        RezervalCompanyFormData data,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rezerval CRM: updating company {CompanyId} ({Name}).", companyId, data.Name);
        await _retryPipeline.ExecuteAsync(async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put,
                $"https://rezback.rezerval.com/v1/Crm/Company/{companyId}");

            ApplyBearerAuth(request, apiKey);
            request.Content = BuildMultipartContent(data);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> from <see cref="RezervalCompanyFormData"/>.
    /// All fields are sent as string parts; the optional logo is added as a file part when present.
    /// </summary>
    private static MultipartFormDataContent BuildMultipartContent(RezervalCompanyFormData data)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(data.Name),                  "Name" },
            { new StringContent(data.Title),                 "Title" },
            { new StringContent(data.Phone),                 "Phone" },
            { new StringContent(data.Email),                 "Email" },
            { new StringContent(data.TaxUnit),               "TaxUnit" },
            { new StringContent(data.TaxNumber),             "TaxNumber" },
            { new StringContent(data.IsPersonCompany ? "true" : "false"), "IsPersonCompany" },
            { new StringContent(data.Address),               "Address" },
            { new StringContent(data.Language.ToString()),   "Language" },
            { new StringContent(data.CountryPhoneCode.ToString()), "CountryPhoneCode" },
            { new StringContent(data.AdminNameSurname),      "AdminNameSurname" },
            { new StringContent(data.AdminLoginName),        "AdminLoginName" },
            { new StringContent(data.AdminPassword),         "AdminPassword" },
            { new StringContent(data.AdminEmail),            "AdminEmail" },
            { new StringContent(data.AdminPhone),            "AdminPhone" },
        };

        if (!string.IsNullOrWhiteSpace(data.TCNo))
            form.Add(new StringContent(data.TCNo), "TCNo");

        if (data.ExperationDate.HasValue)
            form.Add(new StringContent(data.ExperationDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")), "ExperationDate");

        if (data.LogoBytes is not null && data.LogoBytes.Length > 0)
        {
            var logoContent = new ByteArrayContent(data.LogoBytes);
            logoContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(logoContent, "Logo", data.LogoFileName ?? "logo.png");
        }

        return form;
    }

    /// <summary>
    /// Overrides the default X-Api-Key header with a project-specific key when provided.
    /// Falls back to the header pre-configured in DI (appsettings SaasB:ApiKey) when apiKey is null/empty.
    /// </summary>
    private static void ApplyAuth(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
    }

    /// <summary>
    /// Sets Authorization: Bearer header for Rezerval CRM API endpoints that use Bearer token auth.
    /// </summary>
    private static void ApplyBearerAuth(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
    }
}
