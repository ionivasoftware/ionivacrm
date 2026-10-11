using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk social-media post queue. Mirrors <see cref="LiftdeskSiteAnalyticsClient"/>:
/// static Bearer-key auth, responses normalised into <see cref="LiftdeskEnvelope{T}"/>, transport /
/// circuit failures turned into legible Turkish envelopes. Liftdesk's own error messages are English
/// machine constants; the controller maps status codes to operator-facing Turkish.
/// </summary>
public sealed class LiftdeskSocialPostsClient : ILiftdeskSocialPostsClient
{
    private const string DefaultBaseUrl = "https://api.liftdesk.app";
    private const string Root = "/api/v1/crm/social-posts";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskSocialPostsClient> _logger;

    public LiftdeskSocialPostsClient(HttpClient httpClient, IConfiguration configuration, ILogger<LiftdeskSocialPostsClient> logger)
    {
        _httpClient = httpClient; _configuration = configuration; _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    public async Task<LiftdeskEnvelope<LiftdeskSocialPostPage>> ListAsync(
        string? status, string? from, string? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(from))   parts.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrWhiteSpace(to))     parts.Add($"to={Uri.EscapeDataString(to)}");
        using var request = BuildRequest(HttpMethod.Get, $"{Root}?{string.Join("&", parts)}");
        return await SendAsync<LiftdeskSocialPostPage>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<LiftdeskSocialPost>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Get, $"{Root}/{id}");
        return await SendAsync<LiftdeskSocialPost>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<LiftdeskBinary>> GetCardAsync(Guid id, string lang, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Get, $"{Root}/{id}/card?lang={Uri.EscapeDataString(lang)}");
        HttpResponseMessage response;
        try { response = await _httpClient.SendAsync(request, cancellationToken); }
        catch (BrokenCircuitException)
        {
            return new LiftdeskEnvelope<LiftdeskBinary>(false, null,
                "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).", null, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiftdeskEnvelope<LiftdeskBinary>(false, null, "Liftdesk zaman aşımına uğradı. Lütfen tekrar deneyin.", null, 504);
        }

        if (!response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<LiftdeskBinary>(false, null, StatusMessage((int)response.StatusCode), null, (int)response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        return new LiftdeskEnvelope<LiftdeskBinary>(true, new LiftdeskBinary(bytes, contentType), null, null, 200);
    }

    public async Task<LiftdeskEnvelope<LiftdeskSocialPost>> UpdateAsync(
        Guid id, LiftdeskSocialPostUpdate body, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Put, $"{Root}/{id}");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        return await SendAsync<LiftdeskSocialPost>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<LiftdeskSocialPost>> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Post, $"{Root}/{id}/approve");
        return await SendAsync<LiftdeskSocialPost>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<LiftdeskSocialPost>> RejectAsync(
        Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Post, $"{Root}/{id}/reject");
        // Liftdesk gövdeyi nullable okur; sebep yokken de JSON gövde yolla (415'e düşmesin).
        request.Content = JsonContent.Create(new { reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() }, options: JsonOpts);
        return await SendAsync<LiftdeskSocialPost>(request, cancellationToken);
    }

    public async Task<LiftdeskEnvelope<object>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"{Root}/{id}");
        return await SendAsync<object>(request, cancellationToken);
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
                    if (!response.IsSuccessStatusCode)
                        return envelope with { Success = false, StatusCode = (int)response.StatusCode };
                    return envelope;
                }
            }
            catch (JsonException ex)
            {
                parseFailed = true;
                _logger.LogWarning(ex, "Liftdesk: social-posts yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }
        if (parseFailed)
            return new LiftdeskEnvelope<T>(false, default, $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);
        if (response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);

        return new LiftdeskEnvelope<T>(false, default, StatusMessage((int)response.StatusCode), null, (int)response.StatusCode);
    }

    private static string StatusMessage(int status) => status switch
    {
        400 => "Liftdesk isteği geçersiz buldu (400).",
        401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
        404 => "Gönderi bulunamadı (404).",
        409 => "Gönderi bu durumda değiştirilemez (409).",
        503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
        _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {status}",
    };
}
