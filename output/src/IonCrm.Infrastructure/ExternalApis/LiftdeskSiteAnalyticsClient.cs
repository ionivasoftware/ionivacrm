using System.Text;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk corporate-site analytics API. Mirrors <see cref="LiftdeskBackupClient"/>:
/// static Bearer-key auth, responses normalised into <see cref="LiftdeskEnvelope{T}"/>, transport /
/// circuit failures turned into legible Turkish envelopes.
/// </summary>
public sealed class LiftdeskSiteAnalyticsClient : ILiftdeskSiteAnalyticsClient
{
    private const string DefaultBaseUrl = "https://api.liftdesk.app";
    private const string Root = "/api/v1/crm/site-analytics";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskSiteAnalyticsClient> _logger;

    public LiftdeskSiteAnalyticsClient(HttpClient httpClient, IConfiguration configuration, ILogger<LiftdeskSiteAnalyticsClient> logger)
    {
        _httpClient = httpClient; _configuration = configuration; _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    public async Task<LiftdeskEnvelope<LiftdeskSiteAnalyticsOverview>> GetOverviewAsync(
        string? from, string? to, string? site, string? country, CancellationToken cancellationToken = default)
    {
        var q = BuildQuery(from, to, site, country);
        _logger.LogDebug("Liftdesk: fetching site analytics overview {Query}", q);
        using var request = BuildRequest(HttpMethod.Get, $"{Root}/overview{q}");
        return await SendAsync<LiftdeskSiteAnalyticsOverview>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<List<LiftdeskSiteAnalyticsEvent>>> GetEventsAsync(
        string? from, string? to, string? site, string? country, string? type, int limit,
        CancellationToken cancellationToken = default)
    {
        var q = new StringBuilder(BuildQuery(from, to, site, country));
        q.Append(q.Length == 0 ? '?' : '&').Append("limit=").Append(limit);
        if (!string.IsNullOrWhiteSpace(type)) q.Append("&type=").Append(Uri.EscapeDataString(type));
        _logger.LogDebug("Liftdesk: fetching site analytics events {Query}", q);
        using var request = BuildRequest(HttpMethod.Get, $"{Root}/events{q}");
        return await SendAsync<List<LiftdeskSiteAnalyticsEvent>>(request, cancellationToken);
    }

    private static string BuildQuery(string? from, string? to, string? site, string? country)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(from))    parts.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrWhiteSpace(to))      parts.Add($"to={Uri.EscapeDataString(to)}");
        if (!string.IsNullOrWhiteSpace(site))    parts.Add($"site={Uri.EscapeDataString(site)}");
        if (!string.IsNullOrWhiteSpace(country)) parts.Add($"country={Uri.EscapeDataString(country)}");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var baseUrl = _configuration["Liftdesk:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_configuration["Liftdesk:ApiKey"]}");
        return request;
    }

    private async Task<LiftdeskEnvelope<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try { response = await _httpClient.SendAsync(request, cancellationToken); }
        catch (BrokenCircuitException)
        {
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).", null, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiftdeskEnvelope<T>(false, default, "Liftdesk zaman aşımına uğradı. Lütfen tekrar deneyin.", null, 504);
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
                _logger.LogWarning(ex, "Liftdesk: site analytics yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }
        if (parseFailed)
            return new LiftdeskEnvelope<T>(false, default, $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);
        if (response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);

        var message = (int)response.StatusCode switch
        {
            400 => "Geçersiz süzgeç (site tr|en|uk, ülke ISO alfa-2, tarih yyyy-MM-dd, aralık ≤ 400 gün).",
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
            _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {(int)response.StatusCode}",
        };
        return new LiftdeskEnvelope<T>(false, default, message, null, (int)response.StatusCode);
    }
}
