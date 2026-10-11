using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// X API v2 client with OAuth 2.0 PKCE. Config: Social:X:ClientId (+ Social:X:ClientSecret for a
/// confidential app → HTTP Basic on the token endpoint). Access tokens live ~2 h and are refreshed
/// with the rotating refresh token (offline.access). Media goes up as bytes (v2 media/upload), so no
/// public URL is needed for X. 2026 pricing is pay-per-use (~$0.01/post) — negligible at our volume.
/// </summary>
public sealed class XApiClient : IXApiClient
{
    public const string Scopes = "tweet.read tweet.write users.read media.write offline.access";

    private const string AuthorizeUrl = "https://x.com/i/oauth2/authorize";
    private const string TokenUrl = "https://api.x.com/2/oauth2/token";
    private const string ApiBase = "https://api.x.com/2";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<XApiClient> _logger;

    public XApiClient(HttpClient http, IConfiguration configuration, ILogger<XApiClient> logger)
    {
        _http = http; _configuration = configuration; _logger = logger;
    }

    private string? ClientId => _configuration["Social:X:ClientId"];
    private string? ClientSecret => _configuration["Social:X:ClientSecret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
        $"{AuthorizeUrl}?response_type=code&client_id={Uri.EscapeDataString(ClientId ?? string.Empty)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(Scopes)}" +
        $"&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";

    public Task<XTokenResult> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken = default) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier, ["client_id"] = ClientId ?? string.Empty,
        }, cancellationToken);

    public Task<XTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = ClientId ?? string.Empty,
        }, cancellationToken);

    private async Task<XTokenResult> TokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        if (!string.IsNullOrWhiteSpace(ClientSecret))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")));

        var json = await SendAsync(request, ct, tokenEndpoint: true);
        var access = Str(json, "access_token") ?? throw new SocialPublishException("X token yanıtında access_token yok.", isPermanent: true, invalidatesToken: true);
        DateTime? expires = json.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var secs) && secs > 0 ? DateTime.UtcNow.AddSeconds(secs) : null;
        return new XTokenResult(access, Str(json, "refresh_token"), expires, Str(json, "scope"));
    }

    public async Task<XUser> GetMeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = Bearer(HttpMethod.Get, $"{ApiBase}/users/me", accessToken);
        var json = await SendAsync(request, cancellationToken);
        var data = json.TryGetProperty("data", out var d) ? d : json;
        var id = Str(data, "id") ?? throw new SocialPublishException("X kullanıcı yanıtında id yok.", isPermanent: true);
        return new XUser(id, Str(data, "username") ?? id, Str(data, "name") ?? id);
    }

    public async Task<string> UploadImageAsync(string accessToken, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        using var request = Bearer(HttpMethod.Post, $"{ApiBase}/media/upload", accessToken);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType);
        var ext = contentType switch { "image/jpeg" => "jpg", "image/webp" => "webp", _ => "png" };
        request.Content = new MultipartFormDataContent
        {
            { file, "media", $"image.{ext}" },
            { new StringContent("tweet_image"), "media_category" },
        };
        var json = await SendAsync(request, cancellationToken);
        var data = json.TryGetProperty("data", out var d) ? d : json;
        return Str(data, "id") ?? Str(data, "media_id_string") ?? Str(json, "media_id_string")
            ?? throw new SocialPublishException("X medya yüklemesi id döndürmedi.", isPermanent: false);
    }

    public async Task<string> CreatePostAsync(string accessToken, string text, string? mediaId, CancellationToken cancellationToken = default)
    {
        using var request = Bearer(HttpMethod.Post, $"{ApiBase}/tweets", accessToken);
        object body = mediaId is null
            ? new { text }
            : new { text, media = new { media_ids = new[] { mediaId } } };
        request.Content = JsonContent.Create(body, options: JsonOpts);
        var json = await SendAsync(request, cancellationToken);
        var data = json.TryGetProperty("data", out var d) ? d : json;
        return Str(data, "id") ?? throw new SocialPublishException("X gönderisi id döndürmedi.", isPermanent: false);
    }

    // ── Transport ────────────────────────────────────────────────────────────

    private static HttpRequestMessage Bearer(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken ct, bool tokenEndpoint = false)
    {
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new SocialPublishException($"X'e ulaşılamadı: {ex.Message}", isPermanent: false, inner: ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new SocialPublishException("X zaman aşımı.", isPermanent: false, inner: ex); }

        var body = await response.Content.ReadAsStringAsync(ct);
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            if (response.IsSuccessStatusCode) return default;
            throw Classify((int)response.StatusCode, $"HTTP {(int)response.StatusCode}", tokenEndpoint);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = Str(root, "detail") ?? Str(root, "error_description") ?? Str(root, "title") ?? Str(root, "error");
            if (detail is null && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
            {
                var first = errs.EnumerateArray().FirstOrDefault();
                detail = Str(first, "message") ?? Str(first, "detail");
            }
            var ex = Classify((int)response.StatusCode, detail ?? $"HTTP {(int)response.StatusCode}", tokenEndpoint);
            _logger.LogWarning("X API hatası: {Message} (HTTP {Status}, kalıcı={Permanent}, token={Token})",
                ex.Message, (int)response.StatusCode, ex.IsPermanent, ex.InvalidatesToken);
            throw ex;
        }
        return root;
    }

    /// <summary>401 → token dead (re-connect); 403 → tier/permission (account fault); 429/5xx → retry; other 4xx → block.</summary>
    public static SocialPublishException Classify(int httpStatus, string detail, bool tokenEndpoint = false)
    {
        var text = $"X: {detail} (HTTP {httpStatus})";
        return httpStatus switch
        {
            401 => new SocialPublishException(text, isPermanent: true, invalidatesToken: true, providerCode: "401"),
            400 when tokenEndpoint => new SocialPublishException(text, isPermanent: true, invalidatesToken: true, providerCode: "400"),
            403 => new SocialPublishException(text, isPermanent: true, providerCode: "403") { IsAccountFault = true },
            429 => new SocialPublishException(text, isPermanent: false, providerCode: "429"),
            >= 500 => new SocialPublishException(text, isPermanent: false, providerCode: httpStatus.ToString()),
            _ => new SocialPublishException(text, isPermanent: true, providerCode: httpStatus.ToString()),
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
