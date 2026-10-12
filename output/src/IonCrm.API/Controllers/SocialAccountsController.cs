using IonCrm.API.Common;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace IonCrm.API.Controllers;

/// <summary>
/// Sosyal medya hesap bağlama (publisher görev dokümanı §2–§3). Meta (Facebook Page + Instagram) tek
/// OAuth akışıyla, X OAuth 2.0 PKCE ile bağlanır; token'lar yalnız CRM DB'sinde ŞİFRELİ durur, EMS'e
/// hiç gitmez. OAuth geri dönüş uçları anonimdir (tarayıcı platformdan Bearer'sız gelir) — güvenlik
/// tek kullanımlık, sunucuda tutulan <c>state</c> ile sağlanır. SuperAdmin.
/// </summary>
[Route("api/v1/social-accounts")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SocialAccountsController : ApiControllerBase
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(20);
    private static readonly string[] Languages = { "tr", "en", "en-GB" };

    private readonly ISocialAccountRepository _accounts;
    private readonly IMetaGraphClient _meta;
    private readonly IXApiClient _x;
    private readonly ITokenProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SocialAccountsController> _logger;

    public SocialAccountsController(
        ISocialAccountRepository accounts, IMetaGraphClient meta, IXApiClient x, ITokenProtector protector,
        IMemoryCache cache, IConfiguration configuration, ICurrentUserService currentUser, ILogger<SocialAccountsController> logger)
    {
        _accounts = accounts; _meta = meta; _x = x; _protector = protector; _cache = cache;
        _configuration = configuration; _currentUser = currentUser; _logger = logger;
    }

    // ── Liste / yapılandırma ─────────────────────────────────────────────────

    /// <summary>GET /api/v1/social-accounts — bağlı hesaplar (token içermez).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SocialAccountDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var all = await _accounts.GetAllAsync(cancellationToken);
        return OkResponse(all.Select(ToDto).ToList());
    }

    /// <summary>GET /api/v1/social-accounts/config — hangi platform yapılandırılmış, geri dönüş adresleri, yayın politikası.</summary>
    [HttpGet("config")]
    [ProducesResponseType(typeof(ApiResponse<SocialPublisherConfigDto>), StatusCodes.Status200OK)]
    public IActionResult Config()
    {
        var publicBase = PublicApiBaseUrl();
        return OkResponse(new SocialPublisherConfigDto(
            MetaConfigured: _meta.IsConfigured,
            XConfigured: _x.IsConfigured,
            PublicApiBaseUrl: publicBase,
            PublicApiBaseUrlFromConfig: !string.IsNullOrWhiteSpace(_configuration["SocialPosts:PublicApiBaseUrl"]),
            MetaRedirectUri: $"{publicBase}/api/v1/social-accounts/meta/callback",
            XRedirectUri: $"{publicBase}/api/v1/social-accounts/x/callback",
            PublishHourLocal: _configuration.GetValue("SocialPublisher:PublishHourLocal", 10),
            TimeZone: _configuration["SocialPublisher:TimeZone"] ?? "Europe/Istanbul",
            MaxLateDays: _configuration.GetValue("SocialPublisher:MaxLateDays", 2),
            IntervalMinutes: _configuration.GetValue("SocialPublisher:IntervalMinutes", 30),
            PublisherEnabled: _configuration.GetValue("SocialPublisher:Enabled", true),
            TokenKeyConfigured: !string.IsNullOrWhiteSpace(_configuration["Social:TokenKey"]),
            MetaLoginMode: string.IsNullOrWhiteSpace(_configuration["Social:Meta:ConfigId"]) ? "classic" : "business",
            Languages: Languages));
    }

    // ── Meta (Facebook + Instagram) ──────────────────────────────────────────

    /// <summary>POST /api/v1/social-accounts/meta/connect — OAuth yetki adresi; tarayıcı buraya yönlendirilir.</summary>
    [HttpPost("meta/connect")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult MetaConnect()
    {
        if (!_meta.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Meta uygulaması yapılandırılmamış (Social:Meta:AppId / AppSecret).", 400));

        var state = Pkce.CreateState();
        var redirectUri = $"{PublicApiBaseUrl()}/api/v1/social-accounts/meta/callback";
        _cache.Set(MetaKey(state), new MetaPendingState(_currentUser.Email, FrontendOrigin(), redirectUri, null, null, null, null), PendingTtl);
        return OkResponse(new { url = _meta.BuildAuthorizationUrl(redirectUri, state), state });
    }

    /// <summary>
    /// GET /api/v1/social-accounts/meta/callback — Meta'dan dönüş (ANONİM). Kodu uzun ömürlü kullanıcı
    /// token'ına çevirir, sayfaları listeler, state altında saklar ve tarayıcıyı CRM ekranına yönlendirir.
    /// Sayfa seçimi ve kayıt, kimlik doğrulamalı /meta/complete ile tamamlanır.
    /// </summary>
    [HttpGet("meta/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> MetaCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state) || !_cache.TryGetValue(MetaKey(state), out MetaPendingState? pending) || pending is null)
            return Content("Bağlantı isteği bulunamadı ya da süresi doldu. CRM'den yeniden başlatın.", "text/plain; charset=utf-8");

        var target = $"{pending.FrontendOrigin}/admin/social-posts/publisher";
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            _cache.Remove(MetaKey(state));
            return Redirect($"{target}?error={Uri.EscapeDataString($"Meta: {errorDescription ?? error ?? "kod gelmedi"}")}");
        }

        try
        {
            var token = await _meta.ExchangeCodeAsync(code, pending.RedirectUri, cancellationToken);
            var pages = await _meta.GetPagesAsync(token.AccessToken, cancellationToken);
            _cache.Set(MetaKey(state), pending with { UserToken = token.AccessToken, UserTokenExpires = token.ExpiresAt, Pages = pages }, PendingTtl);
            return Redirect($"{target}?meta={Uri.EscapeDataString(state)}");
        }
        catch (SocialPublishException ex)
        {
            _logger.LogWarning(ex, "Meta OAuth değişimi başarısız");
            _cache.Remove(MetaKey(state));
            return Redirect($"{target}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>GET /api/v1/social-accounts/meta/pending/{state} — seçilebilecek sayfalar (+ bağlı Instagram).</summary>
    [HttpGet("meta/pending/{state}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult MetaPending(string state)
    {
        if (!_cache.TryGetValue(MetaKey(state), out MetaPendingState? pending) || pending?.Pages is null)
            return NotFound(ApiResponse<object>.Fail("Bağlantı isteği bulunamadı ya da süresi doldu (20 dk). Yeniden başlatın.", 404));
        return OkResponse(new
        {
            pages = pending.Pages.Select(p => new { pageId = p.Id, name = p.Name, instagramId = p.InstagramId, instagramUsername = p.InstagramUsername }),
            userTokenExpiresAt = pending.UserTokenExpires,
        });
    }

    /// <summary>POST /api/v1/social-accounts/meta/complete — seçilen sayfayı (ve bağlı Instagram'ı) kaydeder.</summary>
    [HttpPost("meta/complete")]
    [ProducesResponseType(typeof(ApiResponse<List<SocialAccountDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MetaComplete([FromBody] MetaCompleteBody body, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(MetaKey(body.State), out MetaPendingState? pending) || pending?.Pages is null)
            return NotFound(ApiResponse<object>.Fail("Bağlantı isteği bulunamadı ya da süresi doldu. Yeniden başlatın.", 404));
        var page = pending.Pages.FirstOrDefault(p => p.Id == body.PageId);
        if (page is null) return BadRequest(ApiResponse<object>.Fail("Seçilen sayfa listede yok.", 400));
        if (!body.ConnectFacebook && !body.ConnectInstagram) return BadRequest(ApiResponse<object>.Fail("En az bir platform seçin.", 400));
        if (body.ConnectInstagram && page.InstagramId is null)
            return BadRequest(ApiResponse<object>.Fail("Bu sayfaya bağlı Instagram Business hesabı yok (Instagram → Ayarlar → Bağlı hesaplar).", 400));
        var language = NormalizeLanguage(body.Language);
        if (body.Language is { Length: > 0 } && language is null) return BadRequest(ApiResponse<object>.Fail("Dil tr | en | en-GB olmalı.", 400));

        var created = new List<SocialAccount>();
        if (body.ConnectFacebook)
            created.Add(await UpsertAsync(SocialProvider.Facebook, page.Id, page.Name, null, page.AccessToken, null, null, MetaGraphClient_Scopes, language,
                string.IsNullOrWhiteSpace(body.Label) ? $"{page.Name} — Facebook" : $"{body.Label.Trim()} — Facebook", cancellationToken));
        if (body.ConnectInstagram)
            created.Add(await UpsertAsync(SocialProvider.Instagram, page.InstagramId!, page.InstagramUsername is null ? null : $"@{page.InstagramUsername}", page.Id, page.AccessToken, null, null, MetaGraphClient_Scopes, language,
                string.IsNullOrWhiteSpace(body.Label) ? $"{page.InstagramUsername ?? page.Name} — Instagram" : $"{body.Label.Trim()} — Instagram", cancellationToken));

        _cache.Remove(MetaKey(body.State));
        return OkResponse(created.Select(ToDto).ToList());
    }

    private const string MetaGraphClient_Scopes = IonCrm.Infrastructure.ExternalApis.MetaGraphClient.Scopes;

    // ── X ────────────────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/social-accounts/x/connect — PKCE yetki adresi.</summary>
    [HttpPost("x/connect")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult XConnect()
    {
        if (!_x.IsConfigured) return BadRequest(ApiResponse<object>.Fail("X uygulaması yapılandırılmamış (Social:X:ClientId).", 400));

        var state = Pkce.CreateState();
        var verifier = Pkce.CreateVerifier();
        var redirectUri = $"{PublicApiBaseUrl()}/api/v1/social-accounts/x/callback";
        _cache.Set(XKey(state), new XPendingState(_currentUser.Email, FrontendOrigin(), redirectUri, verifier), PendingTtl);
        return OkResponse(new { url = _x.BuildAuthorizationUrl(redirectUri, state, Pkce.Challenge(verifier)), state });
    }

    /// <summary>GET /api/v1/social-accounts/x/callback — X'ten dönüş (ANONİM): kod → token, /users/me, kayıt, ekrana yönlendirme.</summary>
    [HttpGet("x/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> XCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state) || !_cache.TryGetValue(XKey(state), out XPendingState? pending) || pending is null)
            return Content("Bağlantı isteği bulunamadı ya da süresi doldu. CRM'den yeniden başlatın.", "text/plain; charset=utf-8");
        _cache.Remove(XKey(state));

        var target = $"{pending.FrontendOrigin}/admin/social-posts/publisher";
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
            return Redirect($"{target}?error={Uri.EscapeDataString($"X: {errorDescription ?? error ?? "kod gelmedi"}")}");

        try
        {
            var token = await _x.ExchangeCodeAsync(code, pending.RedirectUri, pending.CodeVerifier, cancellationToken);
            var me = await _x.GetMeAsync(token.AccessToken, cancellationToken);
            var account = await UpsertAsync(SocialProvider.X, me.Id, $"@{me.Username}", null, token.AccessToken, token.RefreshToken, token.ExpiresAt,
                token.Scope, null, $"@{me.Username} — X", cancellationToken, connectedBy: pending.Email);
            return Redirect($"{target}?connected=x&account={account.Id}");
        }
        catch (SocialPublishException ex)
        {
            _logger.LogWarning(ex, "X OAuth değişimi başarısız");
            return Redirect($"{target}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    // ── Hesap yönetimi ───────────────────────────────────────────────────────

    /// <summary>PUT /api/v1/social-accounts/{id} — etiket, dil eşlemesi, etkin/devre dışı.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SocialAccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSocialAccountBody body, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAsync(id, cancellationToken);
        if (account is null) return NotFound(ApiResponse<object>.Fail("Hesap bulunamadı.", 404));

        if (body.Label is not null)
        {
            if (body.Label.Trim().Length is 0 or > 200) return BadRequest(ApiResponse<object>.Fail("Etiket 1–200 karakter olmalı.", 400));
            account.Label = body.Label.Trim();
        }
        if (body.Language is not null)
        {
            var lang = NormalizeLanguage(body.Language);
            if (body.Language.Length > 0 && lang is null) return BadRequest(ApiResponse<object>.Fail("Dil tr | en | en-GB olmalı (boş = eşleme yok).", 400));
            account.Language = lang;
        }
        if (body.IsEnabled is { } enabled) account.IsEnabled = enabled;

        await _accounts.UpdateAsync(account, cancellationToken);
        return OkResponse(ToDto(account));
    }

    /// <summary>DELETE /api/v1/social-accounts/{id} — bağlantıyı kaldır (token silinir).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _accounts.DeleteAsync(id, cancellationToken);
        return deleted ? OkResponse(new { deleted = true }) : NotFound(ApiResponse<object>.Fail("Hesap bulunamadı.", 404));
    }

    /// <summary>POST /api/v1/social-accounts/{id}/verify — token'ı platformda dener; durumu günceller.</summary>
    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(typeof(ApiResponse<SocialAccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAsync(id, cancellationToken);
        if (account is null) return NotFound(ApiResponse<object>.Fail("Hesap bulunamadı.", 404));

        try
        {
            if (account.Provider == SocialProvider.X)
            {
                var token = await EnsureXTokenAsync(account, cancellationToken);
                var me = await _x.GetMeAsync(token, cancellationToken);
                account.ExternalName = $"@{me.Username}";
            }
            else
            {
                account.ExternalName = await _meta.GetDisplayNameAsync(account.ExternalId, _protector.Unprotect(account.AccessTokenEncrypted), cancellationToken);
                if (account.Provider == SocialProvider.Instagram && !account.ExternalName.StartsWith('@')) account.ExternalName = $"@{account.ExternalName}";
            }
            account.Status = account.TokenExpiresAt is { } exp && account.RefreshTokenEncrypted is null && exp <= DateTime.UtcNow.AddDays(7)
                ? SocialAccountStatus.Expiring : SocialAccountStatus.Active;
            account.LastError = null;
        }
        catch (SocialPublishException ex)
        {
            account.Status = ex.InvalidatesToken ? SocialAccountStatus.Revoked : SocialAccountStatus.Error;
            account.LastError = ex.Message;
        }
        account.LastVerifiedAt = DateTime.UtcNow;
        await _accounts.UpdateAsync(account, cancellationToken);
        return OkResponse(ToDto(account));
    }

    /// <summary>
    /// POST /api/v1/social-accounts/{id}/test — GERÇEK, herkese açık bir test gönderisi atar (kabul ölçütü 1).
    /// Facebook/X metin yeter; Instagram görselsiz gönderi kabul etmediği için imageUrl zorunludur.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestPost(Guid id, [FromBody] TestPostBody body, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAsync(id, cancellationToken);
        if (account is null) return NotFound(ApiResponse<object>.Fail("Hesap bulunamadı.", 404));
        var text = body.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(ApiResponse<object>.Fail("Metin boş olamaz.", 400));
        if (account.Provider == SocialProvider.X && text.Length > 280) return BadRequest(ApiResponse<object>.Fail("X metni 280 karakteri aşamaz.", 400));
        var imageUrl = body.ImageUrl?.Trim();
        if (!string.IsNullOrEmpty(imageUrl) && !(Uri.TryCreate(imageUrl, UriKind.Absolute, out var u) && u.Scheme is "https" or "http"))
            return BadRequest(ApiResponse<object>.Fail("Görsel adresi http(s) ile başlayan tam bir URL olmalı.", 400));
        if (account.Provider == SocialProvider.Instagram && string.IsNullOrEmpty(imageUrl))
            return BadRequest(ApiResponse<object>.Fail("Instagram görselsiz gönderi kabul etmez; herkese açık bir görsel URL'si girin.", 400));

        try
        {
            string platformId;
            switch (account.Provider)
            {
                case SocialProvider.Facebook:
                {
                    var token = _protector.Unprotect(account.AccessTokenEncrypted);
                    platformId = string.IsNullOrEmpty(imageUrl)
                        ? await _meta.PublishFacebookTextAsync(account.ExternalId, token, text, cancellationToken)
                        : await _meta.PublishFacebookPhotoAsync(account.ExternalId, token, imageUrl, text, cancellationToken);
                    break;
                }
                case SocialProvider.Instagram:
                    platformId = await _meta.PublishInstagramImageAsync(account.ExternalId, _protector.Unprotect(account.AccessTokenEncrypted), imageUrl!, text, cancellationToken);
                    break;
                default:
                {
                    var token = await EnsureXTokenAsync(account, cancellationToken);
                    platformId = await _x.CreatePostAsync(token, text, null, cancellationToken);
                    break;
                }
            }
            account.Status = SocialAccountStatus.Active; account.LastError = null; account.LastVerifiedAt = DateTime.UtcNow;
            await _accounts.UpdateAsync(account, cancellationToken);
            return OkResponse(new { platformPostId = platformId });
        }
        catch (SocialPublishException ex)
        {
            if (ex.InvalidatesToken) { account.Status = SocialAccountStatus.Revoked; account.LastError = ex.Message; await _accounts.UpdateAsync(account, cancellationToken); }
            else if (ex.IsAccountFault) { account.Status = SocialAccountStatus.Error; account.LastError = ex.Message; await _accounts.UpdateAsync(account, cancellationToken); }
            return BadRequest(ApiResponse<object>.Fail(ex.Message, 400));
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private async Task<SocialAccount> UpsertAsync(
        SocialProvider provider, string externalId, string? externalName, string? parentExternalId, string accessToken, string? refreshToken,
        DateTime? expiresAt, string? scopes, string? language, string label, CancellationToken ct, string? connectedBy = null)
    {
        // Re-connecting an already-known account refreshes its token in place (keeps label/language/jobs).
        var existing = await _accounts.FindByExternalAsync(provider, externalId, ct);
        var account = existing ?? new SocialAccount { Provider = provider, ExternalId = externalId, Label = label, Language = language };
        account.ExternalName = externalName ?? account.ExternalName;
        account.ParentExternalId = parentExternalId;
        account.AccessTokenEncrypted = _protector.Protect(accessToken);
        account.RefreshTokenEncrypted = refreshToken is null ? null : _protector.Protect(refreshToken);
        account.TokenExpiresAt = expiresAt;
        account.Scopes = scopes;
        account.Status = SocialAccountStatus.Active;
        account.LastError = null;
        account.LastVerifiedAt = DateTime.UtcNow;
        account.IsEnabled = true;
        account.ConnectedBy = connectedBy ?? _currentUser.Email;
        if (existing is null) await _accounts.AddAsync(account, ct); else await _accounts.UpdateAsync(account, ct);
        _logger.LogInformation("Sosyal hesap bağlandı: {Provider} {Label} ({ExternalId}) — {By}", provider, account.Label, externalId, account.ConnectedBy);
        return account;
    }

    private async Task<string> EnsureXTokenAsync(SocialAccount account, CancellationToken ct)
    {
        var access = _protector.Unprotect(account.AccessTokenEncrypted);
        if (account.TokenExpiresAt is not { } exp || exp > DateTime.UtcNow.AddMinutes(5)) return access;
        if (string.IsNullOrEmpty(account.RefreshTokenEncrypted))
            throw new SocialPublishException("X erişim token'ı doldu ve yenileme token'ı yok — yeniden bağlanın.", isPermanent: true, invalidatesToken: true);
        var refreshed = await _x.RefreshAsync(_protector.Unprotect(account.RefreshTokenEncrypted), ct);
        account.AccessTokenEncrypted = _protector.Protect(refreshed.AccessToken);
        if (!string.IsNullOrEmpty(refreshed.RefreshToken)) account.RefreshTokenEncrypted = _protector.Protect(refreshed.RefreshToken);
        account.TokenExpiresAt = refreshed.ExpiresAt;
        await _accounts.UpdateAsync(account, ct);
        return refreshed.AccessToken;
    }

    private static string? NormalizeLanguage(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? null : Languages.FirstOrDefault(l => l.Equals(lang.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string MetaKey(string state) => $"social:meta:{state}";
    private static string XKey(string state) => $"social:x:{state}";

    /// <summary>Config → Railway domain → request (X-Forwarded-Proto + Host). OAuth redirect URIs are built on this and must match the developer-console entry.</summary>
    private string PublicApiBaseUrl()
    {
        var resolved = SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k]);
        if (resolved is not null) return resolved;
        var forwarded = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var scheme = string.IsNullOrWhiteSpace(forwarded) ? Request.Scheme : forwarded.Split(',')[0].Trim();
        return $"{scheme}://{Request.Host}";
    }

    /// <summary>Where to send the browser back after OAuth: config, else the calling frontend's Origin, else the first CORS origin.</summary>
    private string FrontendOrigin()
    {
        var configured = _configuration["SocialPublisher:FrontendBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
        var origin = Request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin)) return origin.TrimEnd('/');
        var cors = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        return (cors.FirstOrDefault(o => o.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ?? cors.FirstOrDefault() ?? "http://localhost:5173").TrimEnd('/');
    }

    internal static SocialAccountDto ToDto(SocialAccount a) => new(
        a.Id, a.Provider.ToString(), a.Label, a.Language, a.ExternalId, a.ExternalName, a.ParentExternalId, a.Status.ToString(),
        a.TokenExpiresAt, a.RefreshTokenEncrypted is not null, a.Scopes, a.LastError, a.LastVerifiedAt, a.IsEnabled, a.ConnectedBy, a.CreatedAt);

    private sealed record MetaPendingState(string Email, string FrontendOrigin, string RedirectUri, string? UserToken, DateTime? UserTokenExpires, List<MetaPage>? Pages, string? Error);
    private sealed record XPendingState(string Email, string FrontendOrigin, string RedirectUri, string CodeVerifier);
}

/// <summary>Bağlı hesap — token İÇERMEZ.</summary>
public record SocialAccountDto(
    Guid Id, string Provider, string Label, string? Language, string ExternalId, string? ExternalName, string? ParentExternalId, string Status,
    DateTime? TokenExpiresAt, bool HasRefreshToken, string? Scopes, string? LastError, DateTime? LastVerifiedAt, bool IsEnabled, string? ConnectedBy, DateTime CreatedAt);

public record SocialPublisherConfigDto(
    bool MetaConfigured, bool XConfigured, string PublicApiBaseUrl, bool PublicApiBaseUrlFromConfig, string MetaRedirectUri, string XRedirectUri,
    int PublishHourLocal, string TimeZone, int MaxLateDays, int IntervalMinutes, bool PublisherEnabled, bool TokenKeyConfigured,
    /// <summary>"business" = Facebook Login for Business (Social:Meta:ConfigId set), "classic" = scope-based dialog.</summary>
    string MetaLoginMode, string[] Languages);

public record MetaCompleteBody(string State, string PageId, string? Language, bool ConnectFacebook, bool ConnectInstagram, string? Label);

public record UpdateSocialAccountBody(string? Label, string? Language, bool? IsEnabled);

public record TestPostBody(string? Text, string? ImageUrl);
