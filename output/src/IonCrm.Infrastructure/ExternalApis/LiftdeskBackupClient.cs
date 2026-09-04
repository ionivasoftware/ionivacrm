using System.Text;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk (EMS) backup-status API. Mirrors <see cref="LiftdeskTicketClient"/>:
/// static Bearer-key auth (Liftdesk:ApiKey) applied per request, responses normalised into
/// <see cref="LiftdeskEnvelope{T}"/>, transport/circuit failures turned into legible Turkish envelopes.
/// </summary>
public sealed class LiftdeskBackupClient : ILiftdeskBackupClient
{
    private const string DefaultBaseUrl = "https://api.liftdesk.app";
    private const string BackupsRoot = "/api/v1/crm/backups";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskBackupClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskBackupClient"/>.</summary>
    public LiftdeskBackupClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LiftdeskBackupClient> logger)
    {
        _httpClient    = httpClient;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    /// <inheritdoc />
    public async Task<LiftdeskEnvelope<LiftdeskBackupStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Liftdesk: fetching backup status.");

        using var request = BuildRequest(HttpMethod.Get, $"{BackupsRoot}/status");
        return await SendAsync<LiftdeskBackupStatus>(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LiftdeskEnvelope<List<LiftdeskBackupRun>>> GetRunsAsync(
        string? kind, int limit, CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder($"?limit={limit}");
        if (!string.IsNullOrWhiteSpace(kind))
            query.Append($"&kind={Uri.EscapeDataString(kind)}");

        _logger.LogDebug("Liftdesk: fetching backup runs. Kind={Kind} Limit={Limit}", kind, limit);

        using var request = BuildRequest(HttpMethod.Get, $"{BackupsRoot}{query}");
        return await SendAsync<List<LiftdeskBackupRun>>(request, cancellationToken);
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
    /// Sends the request and normalises the response into an EMS envelope — same contract as the
    /// ticket client: a parseable envelope is returned as-is (marked failed on non-2xx so the
    /// controller can surface the EMS message), unparseable bodies become a synthetic Turkish
    /// failure. A genuine caller cancellation rethrows; an HttpClient timeout maps to 504.
    /// </summary>
    private async Task<LiftdeskEnvelope<T>> SendAsync<T>(
        HttpRequestMessage request, CancellationToken cancellationToken)
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
                _logger.LogWarning(ex, "Liftdesk: yedek yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }

        if (parseFailed)
            return new LiftdeskEnvelope<T>(false, default,
                $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);

        if (response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);

        var message = (int)response.StatusCode switch
        {
            400 => "Geçersiz istek — koşu türü (kind) tanınmadı.",
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
            _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {(int)response.StatusCode}",
        };
        return new LiftdeskEnvelope<T>(false, default, message, null, (int)response.StatusCode);
    }
}
