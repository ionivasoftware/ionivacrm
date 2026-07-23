using System.Text;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk (EMS) CRM support-chat-logs API. Read-only, static Bearer-key auth
/// (Liftdesk:ApiKey — shared with error-triage/tickets). Responses are normalised into the shared
/// <see cref="LiftdeskEnvelope{T}"/>: a parseable envelope is returned as-is (marked failed on non-2xx
/// so the controller can surface the EMS message), and transport/circuit-breaker/timeout failures
/// become a legible Turkish failure envelope.
/// </summary>
public sealed class LiftdeskSupportChatClient : ILiftdeskSupportChatClient
{
    private const string DefaultBaseUrl = "https://ems-api-development.up.railway.app";
    private const string LogsPath = "/api/v1/crm/support-chat-logs";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskSupportChatClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskSupportChatClient"/>.</summary>
    public LiftdeskSupportChatClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LiftdeskSupportChatClient> logger)
    {
        _httpClient    = httpClient;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    /// <inheritdoc />
    public async Task<LiftdeskEnvelope<LiftdeskPage<LiftdeskSupportChatLog>>> GetLogsAsync(
        Guid? projectId, string? search, string? startDate, string? endDate,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder($"?page={page}&pageSize={pageSize}");
        if (projectId.HasValue)                    query.Append($"&projectId={projectId.Value}");
        if (!string.IsNullOrWhiteSpace(search))    query.Append($"&search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(startDate)) query.Append($"&startDate={Uri.EscapeDataString(startDate)}");
        if (!string.IsNullOrWhiteSpace(endDate))   query.Append($"&endDate={Uri.EscapeDataString(endDate)}");

        _logger.LogDebug("Liftdesk: fetching support-chat logs. Page={Page} Search={HasSearch}",
            page, !string.IsNullOrWhiteSpace(search));

        using var request = BuildRequest(HttpMethod.Get, $"{LogsPath}{query}");
        return await SendAsync<LiftdeskPage<LiftdeskSupportChatLog>>(request, cancellationToken);
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
    /// synthetic failure with a Turkish operator-facing message. A genuine caller cancellation
    /// rethrows; an HttpClient timeout maps to a 504 envelope.
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
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).",
                null, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk zaman aşımına uğradı. Lütfen tekrar deneyin.", null, 504);
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
                _logger.LogWarning(ex, "Liftdesk: support-chat yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }

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
