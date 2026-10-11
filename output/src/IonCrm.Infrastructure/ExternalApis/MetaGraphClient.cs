using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// Meta Graph API client (Facebook Page + Instagram Business). Config: Social:Meta:AppId,
/// Social:Meta:AppSecret, Social:Meta:GraphVersion (default v21.0). Every failure surfaces as
/// <see cref="SocialPublishException"/> classified by Graph error code so the publisher knows whether
/// to retry (rate limit, 5xx), block the job (bad request) or mark the account revoked (OAuth 190…).
/// </summary>
public sealed class MetaGraphClient : IMetaGraphClient
{
    /// <summary>Permissions the Meta app must have passed App Review for (contract §2).</summary>
    public const string Scopes = "pages_show_list,pages_manage_posts,pages_read_engagement,instagram_basic,instagram_content_publish";

    private static readonly TimeSpan ContainerPollDelay = TimeSpan.FromSeconds(2);
    private const int ContainerPollAttempts = 10;

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaGraphClient> _logger;

    public MetaGraphClient(HttpClient http, IConfiguration configuration, ILogger<MetaGraphClient> logger)
    {
        _http = http; _configuration = configuration; _logger = logger;
    }

    private string Version => _configuration["Social:Meta:GraphVersion"] is { Length: > 0 } v ? v : "v21.0";
    private string GraphBase => $"https://graph.facebook.com/{Version}";
    private string? AppId => _configuration["Social:Meta:AppId"];
    private string? AppSecret => _configuration["Social:Meta:AppSecret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);

    public string BuildAuthorizationUrl(string redirectUri, string state) =>
        $"https://www.facebook.com/{Version}/dialog/oauth" +
        $"?client_id={Uri.EscapeDataString(AppId ?? string.Empty)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&state={Uri.EscapeDataString(state)}" +
        $"&scope={Uri.EscapeDataString(Scopes)}" +
        "&response_type=code";

    public async Task<MetaTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var shortLived = await GetAsync(
            $"{GraphBase}/oauth/access_token?client_id={Uri.EscapeDataString(AppId!)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&client_secret={Uri.EscapeDataString(AppSecret!)}&code={Uri.EscapeDataString(code)}", cancellationToken);
        var shortToken = Str(shortLived, "access_token")
            ?? throw new SocialPublishException("Meta kod değişiminde access_token gelmedi.", isPermanent: true);

        // Long-lived user token (~60 days); page tokens derived from it do not expire.
        var longLived = await GetAsync(
            $"{GraphBase}/oauth/access_token?grant_type=fb_exchange_token&client_id={Uri.EscapeDataString(AppId!)}" +
            $"&client_secret={Uri.EscapeDataString(AppSecret!)}&fb_exchange_token={Uri.EscapeDataString(shortToken)}", cancellationToken);
        var token = Str(longLived, "access_token") ?? shortToken;
        DateTime? expires = longLived.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var secs) && secs > 0
            ? DateTime.UtcNow.AddSeconds(secs) : null;
        return new MetaTokenResult(token, expires);
    }

    public async Task<List<MetaPage>> GetPagesAsync(string userToken, CancellationToken cancellationToken = default)
    {
        var json = await GetAsync(
            $"{GraphBase}/me/accounts?fields=id,name,access_token,instagram_business_account%7Bid,username%7D&limit=100" +
            $"&access_token={Uri.EscapeDataString(userToken)}", cancellationToken);
        var pages = new List<MetaPage>();
        if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return pages;
        foreach (var p in data.EnumerateArray())
        {
            var id = Str(p, "id"); var token = Str(p, "access_token");
            if (id is null || token is null) continue;
            string? igId = null, igName = null;
            if (p.TryGetProperty("instagram_business_account", out var ig) && ig.ValueKind == JsonValueKind.Object)
            {
                igId = Str(ig, "id"); igName = Str(ig, "username");
            }
            pages.Add(new MetaPage(id, Str(p, "name") ?? id, token, igId, igName));
        }
        return pages;
    }

    public async Task<string> GetDisplayNameAsync(string nodeId, string token, CancellationToken cancellationToken = default)
    {
        var json = await GetAsync($"{GraphBase}/{Uri.EscapeDataString(nodeId)}?fields=id,name&access_token={Uri.EscapeDataString(token)}", cancellationToken);
        return Str(json, "name") ?? Str(json, "username") ?? nodeId;
    }

    public async Task<string> PublishFacebookPhotoAsync(string pageId, string pageToken, string imageUrl, string message, CancellationToken cancellationToken = default)
    {
        var json = await PostFormAsync($"{GraphBase}/{Uri.EscapeDataString(pageId)}/photos", new Dictionary<string, string>
        {
            ["url"] = imageUrl, ["message"] = message, ["access_token"] = pageToken,
        }, cancellationToken);
        return Str(json, "post_id") ?? Str(json, "id")
            ?? throw new SocialPublishException("Facebook fotoğraf gönderisi id döndürmedi.", isPermanent: false);
    }

    public async Task<string> PublishFacebookTextAsync(string pageId, string pageToken, string message, CancellationToken cancellationToken = default)
    {
        var json = await PostFormAsync($"{GraphBase}/{Uri.EscapeDataString(pageId)}/feed", new Dictionary<string, string>
        {
            ["message"] = message, ["access_token"] = pageToken,
        }, cancellationToken);
        return Str(json, "id") ?? throw new SocialPublishException("Facebook gönderisi id döndürmedi.", isPermanent: false);
    }

    public async Task<string> PublishInstagramImageAsync(string igUserId, string token, string imageUrl, string caption, CancellationToken cancellationToken = default)
    {
        // Step 1 — container. Meta fetches image_url itself: it must be public (contract §4).
        var container = await PostFormAsync($"{GraphBase}/{Uri.EscapeDataString(igUserId)}/media", new Dictionary<string, string>
        {
            ["image_url"] = imageUrl, ["caption"] = caption, ["access_token"] = token,
        }, cancellationToken);
        var containerId = Str(container, "id")
            ?? throw new SocialPublishException("Instagram medya konteyneri id döndürmedi.", isPermanent: false);

        // Images are usually FINISHED immediately; poll briefly in case of IN_PROGRESS.
        for (var i = 0; i < ContainerPollAttempts; i++)
        {
            var status = await GetAsync($"{GraphBase}/{containerId}?fields=status_code&access_token={Uri.EscapeDataString(token)}", cancellationToken);
            var code = Str(status, "status_code") ?? "FINISHED";
            if (code == "FINISHED") break;
            if (code is "ERROR" or "EXPIRED")
                throw new SocialPublishException($"Instagram konteyneri işlenemedi ({code}) — görsel URL'si herkese açık ve geçerli bir JPEG/PNG mi?", isPermanent: true);
            await Task.Delay(ContainerPollDelay, cancellationToken);
        }

        // Step 2 — publish.
        var published = await PostFormAsync($"{GraphBase}/{Uri.EscapeDataString(igUserId)}/media_publish", new Dictionary<string, string>
        {
            ["creation_id"] = containerId, ["access_token"] = token,
        }, cancellationToken);
        return Str(published, "id") ?? throw new SocialPublishException("Instagram yayın adımı id döndürmedi.", isPermanent: false);
    }

    // ── Transport ────────────────────────────────────────────────────────────

    private Task<JsonElement> GetAsync(string url, CancellationToken ct) => SendAsync(new HttpRequestMessage(HttpMethod.Get, url), ct);

    private Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return SendAsync(request, ct);
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        using (request)
        {
            try { response = await _http.SendAsync(request, ct); }
            catch (HttpRequestException ex) { throw new SocialPublishException($"Meta'ya ulaşılamadı: {ex.Message}", isPermanent: false, inner: ex); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new SocialPublishException("Meta zaman aşımı.", isPermanent: false, inner: ex); }
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new SocialPublishException($"Meta beklenmedik yanıt döndü (HTTP {(int)response.StatusCode}).", isPermanent: (int)response.StatusCode is >= 400 and < 500 and not 429);
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            var ex = Classify((int)response.StatusCode, error);
            _logger.LogWarning("Meta Graph hatası: {Message} (HTTP {Status}, kalıcı={Permanent}, token={Token})",
                ex.Message, (int)response.StatusCode, ex.IsPermanent, ex.InvalidatesToken);
            throw ex;
        }
        if (!response.IsSuccessStatusCode)
            throw Classify((int)response.StatusCode, null);
        return root;
    }

    /// <summary>Graph error → retry / block / revoke decision (codes per Meta error reference).</summary>
    public static SocialPublishException Classify(int httpStatus, JsonElement? error)
    {
        int code = 0, subcode = 0; string? message = null, type = null;
        if (error is { ValueKind: JsonValueKind.Object } e)
        {
            if (e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci)) code = ci;
            if (e.TryGetProperty("error_subcode", out var sc) && sc.TryGetInt32(out var si)) subcode = si;
            message = Str(e, "message"); type = Str(e, "type");
        }
        var text = $"Meta: {message ?? type ?? "hata"} (kod {code}{(subcode > 0 ? $"/{subcode}" : "")}, HTTP {httpStatus})";
        var providerCode = subcode > 0 ? $"{code}/{subcode}" : code.ToString();

        // Token invalid / expired / app removed / password changed → re-connect required.
        if (code is 190 or 102 or 459 or 463 or 464 or 467)
            return new SocialPublishException(text, isPermanent: true, invalidatesToken: true, providerCode: providerCode);
        // Permission problems → account-level fault, not a per-post transient.
        if (code == 10 || code is >= 200 and <= 299)
            return new SocialPublishException(text, isPermanent: true, invalidatesToken: false, providerCode: providerCode) { IsAccountFault = true };
        // Throttling / temporary.
        if (code is 1 or 2 or 4 or 17 or 32 or 341 or 613 || httpStatus == 429 || httpStatus >= 500)
            return new SocialPublishException(text, isPermanent: false, providerCode: providerCode);
        return new SocialPublishException(text, isPermanent: true, providerCode: providerCode);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
