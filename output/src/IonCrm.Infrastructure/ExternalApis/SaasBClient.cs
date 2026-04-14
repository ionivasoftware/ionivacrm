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

    // In-memory JWT cache keyed by ApiKey. Tokens are valid for ~1 hour; we refresh at 50 min.
    private readonly Dictionary<string, (string Token, DateTime ExpiresAt)> _tokenCache = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

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
            // a different host (rezback.rezerval.com) and Bearer JWT auth (obtained via GetToken).
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://rezback.rezerval.com/v1/Crm/CompanyList");

            await ApplyBearerAuthAsync(request, apiKey, ct);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            // Rezerval API occasionally returns 200 OK with an empty body — treat as empty list.
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength == 0)
            {
                _logger.LogWarning("Rezerval CRM: CompanyList returned 200 OK with empty body — treating as empty list.");
                return new List<RezervalCompany>();
            }

            string rawBody;
            try { rawBody = await response.Content.ReadAsStringAsync(ct); }
            catch { return new List<RezervalCompany>(); }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogWarning("Rezerval CRM: CompanyList returned 200 OK with whitespace-only body — treating as empty list.");
                return new List<RezervalCompany>();
            }

            // Response is wrapped: {"data":[...], "isSuccess":true, ...}
            var envelope = JsonSerializer.Deserialize<RezervalCompanyListResponse>(rawBody, JsonOpts);
            return envelope?.Data ?? new List<RezervalCompany>();
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

            await ApplyBearerAuthAsync(request, apiKey, ct);
            request.Content = BuildMultipartContent(data);

            var response = await _httpClient.SendAsync(request, ct);
            await EnsureSuccessAsync(response, ct);

            // Response is wrapped: {"data":{"companyId":123,...},"isSuccess":true,...}
            var envelope = await response.Content.ReadFromJsonAsync<RezervalCreateCompanyEnvelope>(JsonOpts, ct);
            var companyData = envelope?.Data;
            if (companyData is null)
            {
                var reason = !string.IsNullOrWhiteSpace(envelope?.Message)
                    ? envelope.Message
                    : "Boş yanıt";
                throw new InvalidOperationException($"Rezerval firma oluşturulamadı: {reason}");
            }
            return new RezervalCreateCompanyResponse(companyData.CompanyId, companyData.Message);
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
                "https://rezback.rezerval.com/v1/Crm/Company");

            await ApplyBearerAuthAsync(request, apiKey, ct);
            request.Content = BuildMultipartContent(data, companyId);

            var response = await _httpClient.SendAsync(request, ct);
            await EnsureSuccessAsync(response, ct);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalCompanySummaryResponse> GetCompanySummaryAsync(
        int companyId,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rezerval CRM: fetching company summary for companyId={CompanyId}.", companyId);

        return await _retryPipeline.ExecuteAsync<RezervalCompanySummaryResponse>(async ct =>
        {
            var http = new HttpRequestMessage(HttpMethod.Get,
                $"https://rezback.rezerval.com/v1/Crm/CompanySummary?companyId={companyId}");

            await ApplyBearerAuthAsync(http, apiKey, ct);

            var response = await _httpClient.SendAsync(http, ct);
            await EnsureSuccessAsync(response, ct);

            var envelope = await response.Content.ReadFromJsonAsync<RezervalCompanySummaryResponse>(JsonOpts, ct);
            if (envelope is null)
                throw new InvalidOperationException("Rezerval şirket özeti yanıtı boş döndü.");

            if (!envelope.IsSuccess)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "Rezerval şirket özeti alınamadı."
                        : $"Rezerval şirket özeti alınamadı: {envelope.Message}");

            return envelope;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalSubscriptionResponse> CreateRezervalSubscriptionAsync(
        RezervalSubscriptionRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Rezerval CRM: creating subscription '{Name}' (companyId={CompanyId}, amount={Amount}, type={Type}).",
            request.SubscriptionName, request.RezervalCompanyId, request.MonthlyAmount, request.PaymentType);

        return await _retryPipeline.ExecuteAsync<RezervalSubscriptionResponse>(async ct =>
        {
            var http = new HttpRequestMessage(HttpMethod.Post,
                "https://rezback.rezerval.com/v1/Crm/Subscription");

            await ApplyBearerAuthAsync(http, apiKey, ct);
            http.Content = JsonContent.Create(request, options: JsonOpts);

            var response = await _httpClient.SendAsync(http, ct);
            await EnsureSuccessAsync(response, ct);

            var envelope = await response.Content.ReadFromJsonAsync<RezervalSubscriptionResponse>(JsonOpts, ct);
            if (envelope is null)
                throw new InvalidOperationException("Rezerval abonelik yanıtı boş döndü.");

            if (!envelope.IsSuccess)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "Rezerval aboneliği oluşturamadı."
                        : $"Rezerval aboneliği oluşturamadı: {envelope.Message}");

            return envelope;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalCancelSubscriptionResponse> CancelRezervalSubscriptionAsync(
        RezervalCancelSubscriptionRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Rezerval CRM: cancelling subscription for companyId={CompanyId}.",
            request.RezervalCompanyId);

        return await _retryPipeline.ExecuteAsync<RezervalCancelSubscriptionResponse>(async ct =>
        {
            var http = new HttpRequestMessage(HttpMethod.Post,
                "https://rezback.rezerval.com/v1/Crm/Subscription/Cancel");

            await ApplyBearerAuthAsync(http, apiKey, ct);
            http.Content = JsonContent.Create(request, options: JsonOpts);

            var response = await _httpClient.SendAsync(http, ct);
            await EnsureSuccessAsync(response, ct);

            var envelope = await response.Content.ReadFromJsonAsync<RezervalCancelSubscriptionResponse>(JsonOpts, ct);
            if (envelope is null)
                throw new InvalidOperationException("Rezerval iptal yanıtı boş döndü.");

            if (!envelope.IsSuccess)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "Rezerval aboneliği iptal edilemedi."
                        : $"Rezerval aboneliği iptal edilemedi: {envelope.Message}");

            // Iyzico-side warnings (e.g. plan already deleted, product missing) are surfaced
            // through envelope.Data.IyzicoWarnings — caller decides whether to display them.
            if (envelope.Data?.IyzicoWarnings is { Count: > 0 } warnings)
            {
                _logger.LogWarning(
                    "Rezerval cancel returned {Count} iyzico warning(s) for companyId={CompanyId}: {Warnings}",
                    warnings.Count, request.RezervalCompanyId, string.Join("; ", warnings));
            }

            return envelope;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalReservationSettingResponse> GetReservationSettingAsync(
        int companyId,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rezerval CRM: fetching reservation setting for companyId={CompanyId}.", companyId);

        return await _retryPipeline.ExecuteAsync<RezervalReservationSettingResponse>(async ct =>
        {
            var http = new HttpRequestMessage(HttpMethod.Get,
                $"https://rezback.rezerval.com/v1/Crm/ReservationSetting?companyId={companyId}");

            await ApplyBearerAuthAsync(http, apiKey, ct);

            var response = await _httpClient.SendAsync(http, ct);
            await EnsureSuccessAsync(response, ct);

            var envelope = await response.Content.ReadFromJsonAsync<RezervalReservationSettingResponse>(JsonOpts, ct);
            if (envelope is null)
                throw new InvalidOperationException("Rezerval rezervasyon ayarı yanıtı boş döndü.");

            if (!envelope.IsSuccess)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "Rezerval rezervasyon ayarı alınamadı."
                        : $"Rezerval rezervasyon ayarı alınamadı: {envelope.Message}");

            return envelope;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RezervalSimpleResponse> UpdateReservationSettingAsync(
        RezervalReservationSettingUpdateRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Rezerval CRM: updating reservation setting for companyId={CompanyId}.",
            request.CompanyId);

        return await _retryPipeline.ExecuteAsync<RezervalSimpleResponse>(async ct =>
        {
            var http = new HttpRequestMessage(HttpMethod.Put,
                "https://rezback.rezerval.com/v1/Crm/ReservationSetting");

            await ApplyBearerAuthAsync(http, apiKey, ct);
            http.Content = JsonContent.Create(request, options: JsonOpts);

            var response = await _httpClient.SendAsync(http, ct);
            await EnsureSuccessAsync(response, ct);

            var envelope = await response.Content.ReadFromJsonAsync<RezervalSimpleResponse>(JsonOpts, ct);
            if (envelope is null)
                throw new InvalidOperationException("Rezerval rezervasyon ayarı güncelleme yanıtı boş döndü.");

            if (!envelope.IsSuccess)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "Rezerval rezervasyon ayarı güncellenemedi."
                        : $"Rezerval rezervasyon ayarı güncellenemedi: {envelope.Message}");

            return envelope;
        }, cancellationToken);
    }

    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> from <see cref="RezervalCompanyFormData"/>.
    /// All fields are sent as string parts; the optional logo is added as a file part when present.
    /// </summary>
    private static MultipartFormDataContent BuildMultipartContent(RezervalCompanyFormData data, int? id = null)
    {
        var form = new MultipartFormDataContent();

        if (id.HasValue)
            form.Add(new StringContent(id.Value.ToString()), "Id");

        form.Add(new StringContent(data.Name),                                    "Name");
        form.Add(new StringContent(data.Title),                                   "Title");
        form.Add(new StringContent(data.Phone),                                   "Phone");
        form.Add(new StringContent(data.Email),                                   "Email");
        form.Add(new StringContent(data.TaxUnit),                                 "TaxUnit");
        form.Add(new StringContent(data.TaxNumber),                               "TaxNumber");
        form.Add(new StringContent(data.IsPersonCompany ? "true" : "false"),      "IsPersonCompany");
        form.Add(new StringContent(data.Address),                                 "Address");
        form.Add(new StringContent(data.Language.ToString()),                     "Language");
        form.Add(new StringContent(data.CountryPhoneCode.ToString()),             "CountryPhoneCode");
        // Only send admin fields when they have values — on update the UI hides
        // admin fields so they arrive as empty strings; sending empty strings to
        // Rezerval would clear the existing admin user.
        if (!string.IsNullOrWhiteSpace(data.AdminNameSurname))
            form.Add(new StringContent(data.AdminNameSurname), "AdminNameSurname");
        if (!string.IsNullOrWhiteSpace(data.AdminLoginName))
            form.Add(new StringContent(data.AdminLoginName), "AdminLoginName");
        if (!string.IsNullOrWhiteSpace(data.AdminPassword))
            form.Add(new StringContent(data.AdminPassword), "AdminPassword");
        if (!string.IsNullOrWhiteSpace(data.AdminEmail))
            form.Add(new StringContent(data.AdminEmail), "AdminEmail");
        if (!string.IsNullOrWhiteSpace(data.AdminPhone))
            form.Add(new StringContent(data.AdminPhone), "AdminPhone");

        if (!string.IsNullOrWhiteSpace(data.TCNo))
            form.Add(new StringContent(data.TCNo), "TCNo");

        if (data.ExperationDate.HasValue)
            form.Add(new StringContent(data.ExperationDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")), "ExperationDate");

        if (data.LogoBytes is not null && data.LogoBytes.Length > 0)
        {
            var logoContent = new ByteArrayContent(data.LogoBytes);
            var fileName = data.LogoFileName ?? "logo.png";
            var mimeType = fileName.ToLowerInvariant() switch
            {
                _ when fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
                _ when fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)  => "image/gif",
                _ when fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) => "image/webp",
                _ => "image/png"
            };
            logoContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            form.Add(logoContent, "Logo", fileName);
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
    /// Exchanges the Rezerval ApiKey for a short-lived JWT via POST /v1/Token/GetToken.
    /// Result is cached per ApiKey for 50 minutes to avoid redundant round-trips.
    /// </summary>
    private async Task<string> GetRezervalJwtAsync(string apiKey, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_tokenCache.TryGetValue(apiKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
                return cached.Token;

            _logger.LogDebug("Rezerval: exchanging ApiKey for JWT.");

            var response = await _httpClient.PostAsJsonAsync(
                "https://rezback.rezerval.com/v1/Token/GetToken",
                new { ApiKey = apiKey },
                JsonOpts,
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RezervalTokenResponse>(JsonOpts, ct);
            var token = result?.Data?.Token
                ?? throw new InvalidOperationException("Rezerval GetToken returned no token.");

            _tokenCache[apiKey] = (token, DateTime.UtcNow.AddMinutes(50));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Throws <see cref="HttpRequestException"/> with the response body included in the message
    /// when the status code indicates failure, for actionable error details.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string body = string.Empty;
        try { body = await response.Content.ReadAsStringAsync(ct); } catch { }

        var detail = string.IsNullOrWhiteSpace(body)
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body.Trim()}";

        throw new HttpRequestException(detail, inner: null, statusCode: response.StatusCode);
    }

    /// <summary>
    /// Sets Authorization: Bearer header using a freshly obtained Rezerval JWT.
    /// </summary>
    private async Task ApplyBearerAuthAsync(HttpRequestMessage request, string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        var jwt = await GetRezervalJwtAsync(apiKey, ct);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
    }
}
