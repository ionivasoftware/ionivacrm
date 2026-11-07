using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for Liftdesk's weekly release notes. Same wire shape as <see cref="LiftdeskSiteBlogClient"/>
/// (static Bearer key, EMS envelope normalised into <see cref="LiftdeskEnvelope{T}"/>, transport failures turned
/// into Turkish envelopes). A successful payload is cached for <see cref="CacheFor"/>: every user's sidebar badge
/// asks for it, while the content changes only when EMS publishes a new release. Failures are never cached.
/// </summary>
public sealed class LiftdeskReleaseNotesClient : ILiftdeskReleaseNotesClient
{
    private const string DefaultBaseUrl = "https://api.liftdesk.app";
    private const string RoutePath = "/api/v1/crm/release-notes";

    /// <summary>Memory-cache key of the last successful envelope.</summary>
    public const string CacheKey = "liftdesk:release-notes";

    /// <summary>How long a successful payload is served from memory.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LiftdeskReleaseNotesClient> _logger;

    public LiftdeskReleaseNotesClient(HttpClient httpClient, IConfiguration configuration, IMemoryCache cache, ILogger<LiftdeskReleaseNotesClient> logger)
    {
        _httpClient = httpClient; _configuration = configuration; _cache = cache; _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    public async Task<LiftdeskEnvelope<LiftdeskReleaseNotes>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out LiftdeskEnvelope<LiftdeskReleaseNotes>? cached) && cached is not null)
            return cached;

        var env = await FetchAsync(cancellationToken);
        if (env.Success && env.Data is not null)
            _cache.Set(CacheKey, env, CacheFor);
        return env;
    }

    private async Task<LiftdeskEnvelope<LiftdeskReleaseNotes>> FetchAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Liftdesk:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}{RoutePath}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_configuration["Liftdesk:ApiKey"]}");

        HttpResponseMessage response;
        try { response = await _httpClient.SendAsync(request, cancellationToken); }
        catch (BrokenCircuitException)
        {
            return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).", null, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, "Liftdesk zaman aşımına uğradı. Lütfen tekrar deneyin.", null, 504);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Liftdesk: sürüm notları alınamadı (ağ).");
            return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, "Liftdesk'e ulaşılamadı. Lütfen tekrar deneyin.", null, 503);
        }

        using (response)
        {
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize<LiftdeskEnvelope<LiftdeskReleaseNotes>>(rawBody, JsonOpts);
                    if (envelope is not null)
                        return response.IsSuccessStatusCode ? envelope : envelope with { Success = false, StatusCode = (int)response.StatusCode };
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Liftdesk: sürüm notları yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
                    return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);
                }
            }
            if (response.IsSuccessStatusCode)
                return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, "Liftdesk boş yanıt döndü.", null, (int)response.StatusCode);
            return new LiftdeskEnvelope<LiftdeskReleaseNotes>(false, null, StatusMessage((int)response.StatusCode), null, (int)response.StatusCode);
        }
    }

    private static string StatusMessage(int status) => status switch
    {
        401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
        404 => "Liftdesk'te sürüm notları ucu yok (404) — EMS sürümü henüz yayınlanmamış olabilir.",
        503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
        _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {status}",
    };
}
