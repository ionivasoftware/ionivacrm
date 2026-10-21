using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk corporate-site blog queue. Same shape as <see cref="LiftdeskSocialPostsClient"/>:
/// static Bearer key, responses normalised into <see cref="LiftdeskEnvelope{T}"/> (incl. <c>errorCode</c> and
/// <c>errors[]</c> from the validator), transport / circuit failures turned into Turkish envelopes.
/// </summary>
public sealed class LiftdeskSiteBlogClient : ILiftdeskSiteBlogClient
{
    private const string DefaultBaseUrl = "https://api.liftdesk.app";
    private const string Root = "/api/v1/crm/site-blog";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiftdeskSiteBlogClient> _logger;

    public LiftdeskSiteBlogClient(HttpClient httpClient, IConfiguration configuration, ILogger<LiftdeskSiteBlogClient> logger)
    {
        _httpClient = httpClient; _configuration = configuration; _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Liftdesk:ApiKey"]);

    public Task<LiftdeskEnvelope<List<LiftdeskSiteLocale>>> GetLocalesAsync(CancellationToken cancellationToken = default) =>
        SendAsync<List<LiftdeskSiteLocale>>(BuildRequest(HttpMethod.Get, $"{Root}/locales"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPage>> ListAsync(string? status, string? locale, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(locale)) parts.Add($"locale={Uri.EscapeDataString(locale)}");
        return SendAsync<LiftdeskSiteBlogPage>(BuildRequest(HttpMethod.Get, $"{Root}?{string.Join("&", parts)}"), cancellationToken);
    }

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> GetAsync(Guid postId, CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSiteBlogPost>(BuildRequest(HttpMethod.Get, $"{Root}/{postId}"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> UpdateVariantAsync(Guid variantId, LiftdeskSiteBlogVariantUpdate body, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Put, $"{Root}/variants/{variantId}");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        return SendAsync<LiftdeskSiteBlogPost>(request, cancellationToken);
    }

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> ApproveVariantAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSiteBlogPost>(BuildRequest(HttpMethod.Post, $"{Root}/variants/{variantId}/approve"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> RejectVariantAsync(Guid variantId, string? reason, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Post, $"{Root}/variants/{variantId}/reject");
        // Gövde nullable okunur; sebep yokken de JSON gövde yolla (415'e düşmesin).
        request.Content = JsonContent.Create(new { reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() }, options: JsonOpts);
        return SendAsync<LiftdeskSiteBlogPost>(request, cancellationToken);
    }

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> RetireVariantAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSiteBlogPost>(BuildRequest(HttpMethod.Post, $"{Root}/variants/{variantId}/retire"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> ApproveAllAsync(Guid postId, CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSiteBlogPost>(BuildRequest(HttpMethod.Post, $"{Root}/{postId}/approve-all"), cancellationToken);

    public Task<LiftdeskEnvelope<object>> DeleteAsync(Guid postId, CancellationToken cancellationToken = default) =>
        SendAsync<object>(BuildRequest(HttpMethod.Delete, $"{Root}/{postId}"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSitePublishResult>> PublishAsync(CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSitePublishResult>(BuildRequest(HttpMethod.Post, $"{Root}/publish"), cancellationToken);

    public Task<LiftdeskEnvelope<LiftdeskSitePublishStatus>> GetPublishStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSitePublishStatus>(BuildRequest(HttpMethod.Get, $"{Root}/publish-status"), cancellationToken);

    public Task<LiftdeskEnvelope<List<LiftdeskSiteBlogTopicRequest>>> GetTopicRequestsAsync(string? status, CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(status) ? $"{Root}/topic-requests" : $"{Root}/topic-requests?status={Uri.EscapeDataString(status)}";
        return SendAsync<List<LiftdeskSiteBlogTopicRequest>>(BuildRequest(HttpMethod.Get, path), cancellationToken);
    }

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogTopicRequest>> CreateTopicRequestAsync(string topic, string? group, string? note, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Post, $"{Root}/topic-requests");
        request.Content = JsonContent.Create(new { topic, group, note }, options: JsonOpts);
        return SendAsync<LiftdeskSiteBlogTopicRequest>(request, cancellationToken);
    }

    public Task<LiftdeskEnvelope<LiftdeskSiteBlogTopicRequest>> DropTopicRequestAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<LiftdeskSiteBlogTopicRequest>(BuildRequest(HttpMethod.Delete, $"{Root}/topic-requests/{id}"), cancellationToken);

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
        using (request)
        {
            HttpResponseMessage response;
            try { response = await _httpClient.SendAsync(request, cancellationToken); }
            catch (BrokenCircuitException)
            {
                return new LiftdeskEnvelope<T>(false, default, "Liftdesk geçici olarak devre dışı (art arda hata alındı, kısa süre sonra otomatik denenecek).", null, 503);
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
                        return response.IsSuccessStatusCode ? envelope : envelope with { Success = false, StatusCode = (int)response.StatusCode };
                }
                catch (JsonException ex)
                {
                    parseFailed = true;
                    _logger.LogWarning(ex, "Liftdesk: site-blog yanıtı çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
                }
            }
            if (parseFailed)
                return new LiftdeskEnvelope<T>(false, default, $"Liftdesk yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);
            if (response.IsSuccessStatusCode)
                return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);
            return new LiftdeskEnvelope<T>(false, default, StatusMessage((int)response.StatusCode), null, (int)response.StatusCode);
        }
    }

    private static string StatusMessage(int status) => status switch
    {
        400 => "Liftdesk isteği geçersiz buldu (400).",
        401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
        404 => "Yazı bulunamadı (404).",
        409 => "Yazı bu durumda değiştirilemez (409).",
        503 => "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503).",
        _   => $"Liftdesk beklenmedik yanıt döndü: HTTP {status}",
    };
}
