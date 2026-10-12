using System.Text.Json;
using IonCrm.API.Common;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Liftdesk sosyal hesap PROFİL metinleri ve görselleri (sayfa hakkında, açıklama, bio, web sitesi…).
/// Varsayılanlar kodda (<see cref="SocialProfileDefaults"/>), operatör CRM'den düzenler. Facebook alanları
/// Pages API ile sayfaya basılır (pages_manage_metadata); Instagram'ın profil yazma API'si yok — metin
/// kopyalanıp uygulamada elle girilir, "uyguladım" işaretlenir. Görseller SocialPostImages'ta durur ve
/// anonim görsel ucundan servis edilir (operatör indirip platforma da yükleyebilir). SuperAdmin.
/// </summary>
[Route("api/v1/social-profiles")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SocialProfilesController : ApiControllerBase
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ISocialProfileRepository _profiles;
    private readonly ISocialPostImageRepository _images;
    private readonly ISocialAccountRepository _accounts;
    private readonly IMetaGraphClient _meta;
    private readonly ITokenProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SocialProfilesController> _logger;

    public SocialProfilesController(
        ISocialProfileRepository profiles, ISocialPostImageRepository images, ISocialAccountRepository accounts, IMetaGraphClient meta,
        ITokenProtector protector, IConfiguration configuration, ICurrentUserService currentUser, ILogger<SocialProfilesController> logger)
    {
        _profiles = profiles; _images = images; _accounts = accounts; _meta = meta; _protector = protector;
        _configuration = configuration; _currentUser = currentUser; _logger = logger;
    }

    /// <summary>GET /api/v1/social-profiles?language=tr — Facebook + Instagram profilleri (kayıt yoksa varsayılan).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SocialProfileDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? language, CancellationToken cancellationToken = default)
    {
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var rows = await _profiles.ListAsync(lang, cancellationToken);
        var accounts = await _accounts.GetAllAsync(cancellationToken);
        var result = new List<SocialProfileDto>();
        foreach (var provider in new[] { SocialProvider.Facebook, SocialProvider.Instagram })
            result.Add(ToDto(provider, lang, rows.FirstOrDefault(r => r.Provider == provider), accounts));
        return OkResponse(result);
    }

    /// <summary>PUT /api/v1/social-profiles/{provider}?language=tr — metinleri kaydet (tam set; bilinmeyen anahtar reddedilir).</summary>
    [HttpPut("{provider}")]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string provider, [FromQuery] string? language, [FromBody] UpdateSocialProfileBody body, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var fields = body.Fields ?? new Dictionary<string, string>();
        if (Validate(p, fields) is { } err) return BadRequest(ApiResponse<object>.Fail(err, 400));

        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        var merged = SocialProfileDefaults.For(p, lang);
        foreach (var (k, v) in fields) merged[k] = v?.Trim() ?? string.Empty;

        if (row is null)
        {
            row = new SocialProfile { Provider = p, Language = lang, FieldsJson = JsonSerializer.Serialize(merged, JsonOpts), UpdatedBy = _currentUser.Email };
            await _profiles.AddAsync(row, cancellationToken);
        }
        else
        {
            row.FieldsJson = JsonSerializer.Serialize(merged, JsonOpts);
            row.UpdatedBy = _currentUser.Email;
            await _profiles.UpdateAsync(row, cancellationToken);
        }
        return OkResponse(ToDto(p, lang, row, await _accounts.GetAllAsync(cancellationToken)));
    }

    /// <summary>POST /api/v1/social-profiles/{provider}/reset?language=tr — kaydı sil, varsayılana dön (görseller de kaldırılır).</summary>
    [HttpPost("{provider}/reset")]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reset(string provider, [FromQuery] string? language, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        if (row is not null)
        {
            if (row.ProfileImageId is { } pi) await _images.DeleteAsync(pi, cancellationToken);
            if (row.CoverImageId is { } ci) await _images.DeleteAsync(ci, cancellationToken);
            await _profiles.DeleteAsync(row.Id, cancellationToken);
        }
        return OkResponse(ToDto(p, lang, null, await _accounts.GetAllAsync(cancellationToken)));
    }

    /// <summary>POST /api/v1/social-profiles/{provider}/images/{slot}?language=tr — profil fotoğrafı (profile) ya da kapak (cover) yükle; PNG/JPEG/WebP ≤ 5 MB.</summary>
    [HttpPost("{provider}/images/{slot}")]
    [RequestSizeLimit(MaxImageBytes + 512 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(string provider, string slot, [FromQuery] string? language, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        if (slot is not ("profile" or "cover")) return BadRequest(ApiResponse<object>.Fail("slot profile | cover olmalı.", 400));
        if (p == SocialProvider.Instagram && slot == "cover") return BadRequest(ApiResponse<object>.Fail("Instagram'da kapak görseli yok.", 400));
        if (file is null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail("Dosya boş.", 400));
        if (file.Length > MaxImageBytes) return BadRequest(ApiResponse<object>.Fail("Görsel en fazla 5 MB olabilir.", 400));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        var contentType = SocialPostImageLinks.SniffImageType(bytes);
        if (contentType is null) return BadRequest(ApiResponse<object>.Fail("Yalnız PNG, JPEG veya WebP görsel yüklenebilir.", 400));

        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        if (row is null)
        {
            row = new SocialProfile { Provider = p, Language = lang, FieldsJson = JsonSerializer.Serialize(SocialProfileDefaults.For(p, lang), JsonOpts), UpdatedBy = _currentUser.Email };
            await _profiles.AddAsync(row, cancellationToken);
        }

        var image = new SocialPostImage
        {
            PostId = row.Id, Kind = slot == "profile" ? "ProfilePicture" : "Cover", Language = lang,
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? null : Path.GetFileName(file.FileName), ContentType = contentType, Content = bytes,
        };
        await _images.AddAsync(image, cancellationToken);

        var old = slot == "profile" ? row.ProfileImageId : row.CoverImageId;
        if (slot == "profile") row.ProfileImageId = image.Id; else row.CoverImageId = image.Id;
        row.UpdatedBy = _currentUser.Email;
        await _profiles.UpdateAsync(row, cancellationToken);
        if (old is { } oldId && oldId != image.Id) await _images.DeleteAsync(oldId, cancellationToken);

        return OkResponse(ToDto(p, lang, row, await _accounts.GetAllAsync(cancellationToken)));
    }

    /// <summary>DELETE /api/v1/social-profiles/{provider}/images/{slot}?language=tr</summary>
    [HttpDelete("{provider}/images/{slot}")]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteImage(string provider, string slot, [FromQuery] string? language, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        if (slot is not ("profile" or "cover")) return BadRequest(ApiResponse<object>.Fail("slot profile | cover olmalı.", 400));
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        if (row is not null)
        {
            var id = slot == "profile" ? row.ProfileImageId : row.CoverImageId;
            if (slot == "profile") row.ProfileImageId = null; else row.CoverImageId = null;
            await _profiles.UpdateAsync(row, cancellationToken);
            if (id is { } imageId) await _images.DeleteAsync(imageId, cancellationToken);
        }
        return OkResponse(ToDto(p, lang, row, await _accounts.GetAllAsync(cancellationToken)));
    }

    /// <summary>
    /// POST /api/v1/social-profiles/facebook/apply?language=tr — hakkında / açıklama / web / telefon / e-posta
    /// alanlarını ve varsa profil fotoğrafını bağlı Facebook sayfasına basar. Kapak fotoğrafı API ile
    /// ayarlanamaz (elle). Instagram için 400: profil yazma API'si yok, "mark-applied" kullanılır.
    /// </summary>
    [HttpPost("{provider}/apply")]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(string provider, [FromQuery] string? language, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        if (p != SocialProvider.Facebook)
            return BadRequest(ApiResponse<object>.Fail("Instagram profil bilgileri API ile yazılamıyor; metinleri kopyalayıp uygulamada girin, sonra 'Uyguladım' işaretleyin.", 400));
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;

        var accounts = await _accounts.GetAllAsync(cancellationToken);
        var account = PickAccount(accounts, p, lang);
        if (account is null)
            return BadRequest(ApiResponse<object>.Fail("Bu dile eşlenmiş, etkin bir Facebook sayfası bağlı değil (Yayınlama & Hesaplar).", 400));

        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        var fields = row is null ? SocialProfileDefaults.For(p, lang) : ReadFields(row.FieldsJson);
        if (row is null)
        {
            row = new SocialProfile { Provider = p, Language = lang, FieldsJson = JsonSerializer.Serialize(fields, JsonOpts), UpdatedBy = _currentUser.Email };
            await _profiles.AddAsync(row, cancellationToken);
        }

        try
        {
            var token = _protector.Unprotect(account.AccessTokenEncrypted);
            var pushable = fields.Where(kv => SocialProfileDefaults.FacebookPushableKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            await _meta.UpdatePageAsync(account.ExternalId, token, pushable, cancellationToken);
            if (row.ProfileImageId is { } imageId && await _images.GetAsync(imageId, cancellationToken) is { } image)
                await _meta.SetPagePictureAsync(account.ExternalId, token, image.Content, image.ContentType, cancellationToken);

            row.AppliedAt = DateTime.UtcNow; row.ApplyError = null;
            _logger.LogInformation("Facebook sayfa profili güncellendi: {Page} ({Fields})", account.Label, string.Join(",", pushable.Keys));
        }
        catch (SocialPublishException ex)
        {
            row.ApplyError = ex.ProviderCode is "10" or "200" or "210" || ex.IsAccountFault
                ? $"{ex.Message} — Facebook Login for Business yapılandırmasına 'pages_manage_metadata' iznini ekleyip hesabı yeniden bağlayın."
                : ex.Message;
            await _profiles.UpdateAsync(row, cancellationToken);
            return BadRequest(ApiResponse<object>.Fail(row.ApplyError, 400));
        }
        await _profiles.UpdateAsync(row, cancellationToken);
        return OkResponse(ToDto(p, lang, row, accounts));
    }

    /// <summary>POST /api/v1/social-profiles/{provider}/mark-applied?language=tr — elle uyguladım (Instagram; Facebook kapak için de).</summary>
    [HttpPost("{provider}/mark-applied")]
    [ProducesResponseType(typeof(ApiResponse<SocialProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkApplied(string provider, [FromQuery] string? language, CancellationToken cancellationToken = default)
    {
        if (ParseProvider(provider) is not { } p) return BadRequest(ApiResponse<object>.Fail("provider facebook | instagram olmalı.", 400));
        var lang = NormalizeLanguage(language) ?? SocialProfileDefaults.DefaultLanguage;
        var row = await _profiles.GetAsync(p, lang, cancellationToken);
        if (row is null)
        {
            row = new SocialProfile { Provider = p, Language = lang, FieldsJson = JsonSerializer.Serialize(SocialProfileDefaults.For(p, lang), JsonOpts), UpdatedBy = _currentUser.Email };
            await _profiles.AddAsync(row, cancellationToken);
        }
        row.AppliedAt = DateTime.UtcNow; row.ApplyError = null; row.UpdatedBy = _currentUser.Email;
        await _profiles.UpdateAsync(row, cancellationToken);
        return OkResponse(ToDto(p, lang, row, await _accounts.GetAllAsync(cancellationToken)));
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    public static string? Validate(SocialProvider provider, Dictionary<string, string> fields)
    {
        var allowed = SocialProfileDefaults.KeysFor(provider);
        foreach (var (key, value) in fields)
        {
            if (!allowed.Contains(key)) return $"Bilinmeyen alan: {key}.";
            var v = value ?? string.Empty;
            if (v.Length > 5000) return $"{key}: en fazla 5000 karakter.";
            if (provider == SocialProvider.Facebook && key == "about" && v.Trim().Length > SocialProfileDefaults.FacebookAboutMax)
                return $"Hakkında (about) en fazla {SocialProfileDefaults.FacebookAboutMax} karakter olabilir.";
            if (provider == SocialProvider.Instagram && key == "bio" && v.Trim().Length > SocialProfileDefaults.InstagramBioMax)
                return $"Bio en fazla {SocialProfileDefaults.InstagramBioMax} karakter olabilir.";
            if (provider == SocialProvider.Instagram && key == "name" && v.Trim().Length > SocialProfileDefaults.InstagramNameMax)
                return $"Ad en fazla {SocialProfileDefaults.InstagramNameMax} karakter olabilir.";
            if (key is "website" && !string.IsNullOrWhiteSpace(v) && !(Uri.TryCreate(v.Trim(), UriKind.Absolute, out var u) && u.Scheme is "https" or "http"))
                return "Web sitesi http(s) ile başlayan tam bir URL olmalı.";
        }
        return null;
    }

    private static SocialAccount? PickAccount(List<SocialAccount> accounts, SocialProvider provider, string lang) =>
        accounts.Where(a => a.Provider == provider && a.IsEnabled && a.Status is SocialAccountStatus.Active or SocialAccountStatus.Expiring)
            .OrderByDescending(a => string.Equals(a.Language, lang, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static Dictionary<string, string> ReadFields(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static SocialProvider? ParseProvider(string s) => s.ToLowerInvariant() switch
    {
        "facebook" => SocialProvider.Facebook,
        "instagram" => SocialProvider.Instagram,
        _ => null,
    };

    private static string? NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        return new[] { "tr", "en", "en-GB" }.FirstOrDefault(l => l.Equals(lang.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string? PublicBase() => SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k])
        ?? $"{(Request.Headers["X-Forwarded-Proto"].FirstOrDefault() is { Length: > 0 } f ? f.Split(',')[0].Trim() : Request.Scheme)}://{Request.Host}";

    private SocialProfileDto ToDto(SocialProvider provider, string lang, SocialProfile? row, List<SocialAccount> accounts)
    {
        var defaults = SocialProfileDefaults.For(provider, lang);
        var fields = row is null ? defaults : ReadFields(row.FieldsJson);
        foreach (var k in SocialProfileDefaults.KeysFor(provider)) fields.TryAdd(k, defaults.GetValueOrDefault(k, string.Empty));
        var account = PickAccount(accounts, provider, lang);
        var baseUrl = PublicBase();
        return new SocialProfileDto(
            provider.ToString(), lang, fields, IsDefault: row is null,
            ProfileImageUrl: row?.ProfileImageId is { } pi ? SocialPostImageLinks.Build(baseUrl!, pi) : null,
            CoverImageUrl: row?.CoverImageId is { } ci ? SocialPostImageLinks.Build(baseUrl!, ci) : null,
            AppliedAt: row?.AppliedAt, ApplyError: row?.ApplyError, UpdatedAt: row?.UpdatedAt, UpdatedBy: row?.UpdatedBy,
            CanApply: provider == SocialProvider.Facebook && account is not null,
            ConnectedAccountLabel: account?.Label,
            PushableKeys: provider == SocialProvider.Facebook ? SocialProfileDefaults.FacebookPushableKeys : Array.Empty<string>());
    }
}

public record SocialProfileDto(
    string Provider, string Language, Dictionary<string, string> Fields, bool IsDefault, string? ProfileImageUrl, string? CoverImageUrl,
    DateTime? AppliedAt, string? ApplyError, DateTime? UpdatedAt, string? UpdatedBy, bool CanApply, string? ConnectedAccountLabel, string[] PushableKeys);

public record UpdateSocialProfileBody(Dictionary<string, string>? Fields);
