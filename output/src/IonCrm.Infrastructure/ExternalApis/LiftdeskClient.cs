using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk (EMS) error-triage API.
/// Static Bearer-key auth (no token exchange) — the shared M2M key lives in Liftdesk:ApiKey and is
/// applied per request server-side. Non-2xx responses that still carry the EMS envelope (401/404/409/503)
/// are returned as failed envelopes so the controller can surface the EMS message.
/// </summary>
public sealed class LiftdeskClient : ILiftdeskClient
{
    private const string DefaultBaseUrl = "https://ems-api-development.up.railway.app";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskClient"/>.</summary>
    public LiftdeskClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LiftdeskClient> logger)
    {
        _httpClient    = httpClient;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    /// <inheritdoc />
    public async Task<LiftdeskEnvelope<LiftdeskPage<LiftdeskErrorAnalysis>>> GetErrorAnalysesAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(status))
            query += $"&status={Uri.EscapeDataString(status)}";

        _logger.LogDebug("Liftdesk: fetching error analyses. Status={Status} Page={Page} PageSize={PageSize}",
            status, page, pageSize);

        using var request = BuildRequest(HttpMethod.Get, $"/api/v1/crm/error-analyses{query}");
        return await SendAsync<LiftdeskPage<LiftdeskErrorAnalysis>>(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LiftdeskEnvelope<LiftdeskErrorAnalysis>> UpdateErrorAnalysisStatusAsync(
        Guid id,
        string status,
        string? approvedBy,
        string? rejectReason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Liftdesk: updating error analysis {Id} → {Status}", id, status);

        using var request = BuildRequest(HttpMethod.Patch, $"/api/v1/crm/error-analyses/{id}/status");

        // EMS contract: approval carries approvedBy, rejection carries rejectReason.
        object body = string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)
            ? new { status, rejectReason }
            : new { status, approvedBy };
        request.Content = JsonContent.Create(body, options: JsonOpts);

        return await SendAsync<LiftdeskErrorAnalysis>(request, cancellationToken);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var baseUrl = _configuration["Liftdesk:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_configuration["Liftdesk:ApiKey"]}");
        return request;
    }

    /// <summary>
    /// Sends the request and normalises the response into an EMS envelope. A parseable envelope is
    /// returned as-is (marked failed when the HTTP status is non-2xx); unparseable bodies become a
    /// synthetic failure with a Turkish operator-facing message.
    /// </summary>
    private async Task<LiftdeskEnvelope<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            // The shared circuit breaker opened (5 consecutive transport/5xx failures). Return a
            // stable operator-facing message instead of leaking Polly's raw English exception text.
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).",
                null, 503);
        }

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var parseFailed = false;

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<LiftdeskEnvelope<T>>(rawBody, JsonOpts);
                if (envelope is not null)
                {
                    if (!response.IsSuccessStatusCode && envelope.Success)
                        return envelope with { Success = false, StatusCode = (int)response.StatusCode };
                    return envelope;
                }
            }
            catch (JsonException ex)
            {
                parseFailed = true;
                _logger.LogWarning(ex, "Liftdesk: yanıt gövdesi çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }

        // A 2xx body we couldn't parse is a contract break, not an empty queue — fail loudly so the
        // UI shows a warning instead of silently rendering zero Liftdesk cards.
        if (parseFailed)
            return new LiftdeskEnvelope<T>(false, default,
                $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);

        if (response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);

        var message = (int)response.StatusCode switch
        {
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
            _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {(int)response.StatusCode}",
        };
        return new LiftdeskEnvelope<T>(false, default, message, null, (int)response.StatusCode);
    }
}
