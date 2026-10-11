using System.Text.RegularExpressions;
using IonCrm.API.Common;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Liftdesk sosyal medya gönderi kuyruğu — onay ekranı (docs/crm-social-media-api.md, Liftdesk deposu).
/// Liftdesk uçlarını proxy'ler; statik CRM anahtarı sunucuda kalır. SuperAdmin'e açık (Liftdesk'in
/// kendi pazarlama hesapları). Görsel ezme: dosya CRM'de saklanır (SocialPostImages), Liftdesk'e
/// yalnız URL yazılır; görsel ucu anonimdir çünkü publisher/platformlar CRM kimliği olmadan çeker.
/// Yayınlama bu API'de YOK — onaylanan kayıt publisher kuyruğudur.
/// </summary>
[Route("api/v1/social-posts")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SocialPostsController : ApiControllerBase
{
    private const long MaxImageBytes = 5 * 1024 * 1024;

    private static readonly Regex DateRe = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly string[] Statuses  = { "Pending", "Approved", "Rejected", "Published" };
    private static readonly string[] Languages = { "tr", "en", "en-GB" };

    private readonly ILiftdeskSocialPostsClient _client;
    private readonly ISocialPostImageRepository _images;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialPostsController> _logger;

    public SocialPostsController(
        ILiftdeskSocialPostsClient client,
        ISocialPostImageRepository images,
        IConfiguration configuration,
        ILogger<SocialPostsController> logger)
    {
        _client = client; _images = images; _configuration = configuration; _logger = logger;
    }

    // ── Liste / sayım ────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/social-posts?status=&amp;from=&amp;to=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPostPage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;

        string? normStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normStatus = Statuses.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normStatus is null)
                return BadRequest(ApiResponse<object>.Fail("status Pending | Approved | Rejected | Published olmalı.", 400));
        }
        if (!string.IsNullOrWhiteSpace(from) && !DateRe.IsMatch(from)) return BadRequest(ApiResponse<object>.Fail("from yyyy-MM-dd olmalı.", 400));
        if (!string.IsNullOrWhiteSpace(to) && !DateRe.IsMatch(to))     return BadRequest(ApiResponse<object>.Fail("to yyyy-MM-dd olmalı.", 400));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var env = await _client.ListAsync(normStatus, from, to, page, pageSize, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Gönderi listesi alınamadı.");
        return OkResponse(env.Data);
    }

    /// <summary>GET /api/v1/social-posts/pending-count — menü rozeti (günlük rutin: sabah girip onaylamak).</summary>
    [HttpGet("pending-count")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingCount(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured) return OkResponse(new { count = 0 });
        var env = await _client.ListAsync("Pending", null, null, 1, 1, cancellationToken);
        // Rozet için hata gürültüsü istemeyiz: Liftdesk erişilemezse 0 döner, ekran kendi hatasını gösterir.
        return OkResponse(new { count = env.Success && env.Data is not null ? env.Data.TotalCount : 0 });
    }

    // ── Detay / kart ─────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/social-posts/{id}</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPost>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.GetAsync(id, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Gönderi alınamadı.");
        return OkResponse(env.Data);
    }

    /// <summary>GET /api/v1/social-posts/{id}/card?lang=tr — şablon kart PNG'si (1080×1080), o dilin başlığıyla.</summary>
    [HttpGet("{id:guid}/card")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Card(Guid id, [FromQuery] string? lang, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var language = NormalizeLanguage(lang);
        if (language is null) return BadRequest(ApiResponse<object>.Fail("lang tr | en | en-GB olmalı.", 400));

        var env = await _client.GetCardAsync(id, language, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Kart görseli alınamadı.", "Bu dilde varyant yok; kart üretilemedi.");
        Response.Headers.CacheControl = "no-store"; // başlık değişince kart da değişir
        return File(env.Data.Content, env.Data.ContentType);
    }

    // ── Düzenleme ────────────────────────────────────────────────────────────

    /// <summary>PUT /api/v1/social-posts/{id} — yalnız Pending/Rejected. customImageUrl "" = şablon karta dön.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPost>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSocialPostBody body, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        if (ValidateUpdate(body) is { } err) return BadRequest(ApiResponse<object>.Fail(err, 400));

        // Görsel değişiyorsa eski (bizim) blob'u sonradan silebilmek için mevcut URL'yi al.
        Guid? previousOwnImage = null;
        if (body.CustomImageUrl is not null)
        {
            var current = await _client.GetAsync(id, cancellationToken);
            if (!current.Success || current.Data is null) return Fail(current, "Gönderi alınamadı.");
            previousOwnImage = SocialPostImageLinks.TryParseImageId(current.Data.CustomImageUrl);
        }

        var env = await _client.UpdateAsync(id, ToUpdate(body), cancellationToken);
        if (!env.Success || env.Data is null)
            return Fail(env, "Gönderi güncellenemedi.", "Onaylı veya yayınlanmış gönderi düzenlenemez. Önce \"Geri Al\" ile reddedin.");

        if (previousOwnImage is { } old && SocialPostImageLinks.TryParseImageId(env.Data.CustomImageUrl) != old)
            await TryDeleteImage(old, cancellationToken);

        return OkResponse(env.Data);
    }

    /// <summary>
    /// POST /api/v1/social-posts/{id}/image — "Kendi görselini yükle": dosya CRM'de saklanır, URL'i
    /// Liftdesk'e customImageUrl olarak yazılır. PNG/JPEG/WebP, ≤ 5 MB; tür ilk baytlardan doğrulanır.
    /// </summary>
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(MaxImageBytes + 512 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPost>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        if (file is null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail("Dosya boş.", 400));
        if (file.Length > MaxImageBytes)      return BadRequest(ApiResponse<object>.Fail("Görsel en fazla 5 MB olabilir.", 400));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        var contentType = SocialPostImageLinks.SniffImageType(bytes);
        if (contentType is null)
            return BadRequest(ApiResponse<object>.Fail("Yalnız PNG, JPEG veya WebP görsel yüklenebilir.", 400));

        var current = await _client.GetAsync(id, cancellationToken);
        if (!current.Success || current.Data is null) return Fail(current, "Gönderi alınamadı.");
        if (current.Data.Status is not ("Pending" or "Rejected"))
            return StatusCode(409, ApiResponse<object>.Fail("Onaylı veya yayınlanmış gönderinin görseli değiştirilemez. Önce \"Geri Al\" ile reddedin.", 409));

        var image = new SocialPostImage
        {
            PostId = id,
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? null : Path.GetFileName(file.FileName),
            ContentType = contentType,
            Content = bytes,
        };
        await _images.AddAsync(image, cancellationToken);
        var url = SocialPostImageLinks.Build(PublicApiBaseUrl(), image.Id);

        var env = await _client.UpdateAsync(id, new LiftdeskSocialPostUpdate(null, url, null), cancellationToken);
        if (!env.Success || env.Data is null)
        {
            await TryDeleteImage(image.Id, cancellationToken); // yetim blob bırakma
            return Fail(env, "Görsel Liftdesk'e yazılamadı.", "Gönderi bu arada onaylanmış; görsel değiştirilemedi.");
        }

        var previous = SocialPostImageLinks.TryParseImageId(current.Data.CustomImageUrl);
        if (previous is { } old && old != image.Id) await TryDeleteImage(old, cancellationToken);

        return OkResponse(env.Data);
    }

    /// <summary>
    /// GET /api/v1/social-posts/images/{imageId} — ANONİM: publisher ve platformlar çeker. Kimlik
    /// yalnız tahmin edilemez GUID; içerik yazıldıktan sonra değişmez, bu yüzden uzun süre önbelleklenir.
    /// </summary>
    [HttpGet("images/{imageId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetAsync(imageId, cancellationToken);
        if (image is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image.Content, image.ContentType);
    }

    // ── Durum geçişleri ──────────────────────────────────────────────────────

    /// <summary>POST /api/v1/social-posts/{id}/approve — Pending/Rejected → Approved (publisher kuyruğu).</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPost>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.ApproveAsync(id, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Gönderi onaylanamadı.", "Gönderi onaylanabilir durumda değil (zaten onaylı ya da yayınlanmış).");
        return OkResponse(env.Data);
    }

    /// <summary>POST /api/v1/social-posts/{id}/reject — Pending/Approved → Rejected. Onaylıda "Geri Al" budur.</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSocialPost>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectSocialPostBody? body, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var reason = body?.Reason?.Trim();
        if (reason is { Length: > 500 }) return BadRequest(ApiResponse<object>.Fail("Ret sebebi en fazla 500 karakter olabilir.", 400));

        var env = await _client.RejectAsync(id, string.IsNullOrEmpty(reason) ? null : reason, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Gönderi reddedilemedi.", "Gönderi reddedilebilir durumda değil (zaten reddedilmiş ya da yayınlanmış).");
        return OkResponse(env.Data);
    }

    /// <summary>DELETE /api/v1/social-posts/{id} — slot boşalır, ajan yarınki koşuda YENİ taslak üretir. Published silinemez.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;

        // Bizim görselimiz varsa silinen kaydın ardından blob'u da kaldır (en iyi çaba).
        Guid? ownImage = null;
        var current = await _client.GetAsync(id, cancellationToken);
        if (current.Success && current.Data is not null)
            ownImage = SocialPostImageLinks.TryParseImageId(current.Data.CustomImageUrl);

        var env = await _client.DeleteAsync(id, cancellationToken);
        if (!env.Success) return Fail(env, "Gönderi silinemedi.", "Gönderi silinemedi.", notFound: "Gönderi bulunamadı ya da yayınlanmış (yayınlanan silinemez).");

        if (ownImage is { } old) await TryDeleteImage(old, cancellationToken);
        return OkResponse(new { deleted = true });
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private IActionResult? NotConfigured() =>
        _client.IsConfigured ? null : BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

    /// <summary>Liftdesk zarfını operatör diline ve doğru HTTP koduna çevirir.</summary>
    private IActionResult Fail<T>(LiftdeskEnvelope<T> env, string fallback, string? conflict = null, string? notFound = null)
    {
        var code = env.StatusCode is 400 or 401 or 404 or 409 or 503 or 504 ? env.StatusCode : 400;
        var message = code switch
        {
            404 => notFound ?? "Gönderi bulunamadı (silinmiş olabilir).",
            409 => conflict ?? "Gönderi bu durumda değiştirilemez.",
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 or 504 => env.Message ?? fallback,
            400 => string.IsNullOrWhiteSpace(env.Message) ? fallback : $"{fallback} ({env.Message})",
            _ => fallback,
        };
        return StatusCode(code, ApiResponse<object>.Fail(message, code));
    }

    private static string? NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        return Languages.FirstOrDefault(l => l.Equals(lang.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Liftdesk'in FluentValidation sınırlarının aynısı — 400'ü Türkçe ve istek atılmadan ver.</summary>
    internal static string? ValidateUpdate(UpdateSocialPostBody body)
    {
        if (body.Topic is { Length: > 300 }) return "Konu en fazla 300 karakter olabilir.";
        if (body.CustomImageUrl is { Length: > 1000 }) return "Görsel adresi en fazla 1000 karakter olabilir.";
        if (!string.IsNullOrEmpty(body.CustomImageUrl) &&
            !(Uri.TryCreate(body.CustomImageUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")))
            return "Görsel adresi http(s) ile başlayan tam bir URL olmalı.";

        if (body.Variants is null) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in body.Variants)
        {
            var lang = NormalizeLanguage(v.Language);
            if (lang is null) return "Dil tr | en | en-GB olmalı.";
            if (!seen.Add(lang)) return $"{lang} dili birden fazla kez gönderildi.";
            if (string.IsNullOrWhiteSpace(v.CardTitle)) return $"{lang}: kart başlığı boş olamaz.";
            if (v.CardTitle.Trim().Length > 80) return $"{lang}: kart başlığı en fazla 80 karakter olabilir.";
            if (string.IsNullOrWhiteSpace(v.Body)) return $"{lang}: gönderi metni boş olamaz.";
            if (v.Body.Trim().Length > 4000) return $"{lang}: gönderi metni en fazla 4000 karakter olabilir.";
            if (string.IsNullOrWhiteSpace(v.BodyShort)) return $"{lang}: X metni boş olamaz.";
            if (v.BodyShort.Trim().Length > 280) return $"{lang}: X metni hashtag'ler dâhil en fazla 280 karakter olabilir.";
            if (v.Hashtags is { Length: > 300 }) return $"{lang}: etiketler en fazla 300 karakter olabilir.";
        }
        return null;
    }

    private static LiftdeskSocialPostUpdate ToUpdate(UpdateSocialPostBody body) => new(
        body.Topic?.Trim(),
        body.CustomImageUrl is null ? null : body.CustomImageUrl.Trim(),
        body.Variants?.Select(v => new LiftdeskSocialPostVariant(
            NormalizeLanguage(v.Language)!,
            v.CardTitle.Trim(),
            v.Body.Trim(),
            v.BodyShort.Trim(),
            (v.Hashtags ?? string.Empty).Trim())).ToList());

    /// <summary>
    /// Görsel bağlantılarının kökü. Railway TLS'i proxy'de sonlandırır (Request.Scheme "http" görünür),
    /// bu yüzden X-Forwarded-Proto okunur; sabit bir alan adı istenirse SocialPosts:PublicApiBaseUrl.
    /// </summary>
    private string PublicApiBaseUrl()
    {
        var configured = _configuration["SocialPosts:PublicApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

        var forwarded = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var scheme = string.IsNullOrWhiteSpace(forwarded) ? Request.Scheme : forwarded.Split(',')[0].Trim();
        return $"{scheme}://{Request.Host}";
    }

    private async Task TryDeleteImage(Guid imageId, CancellationToken cancellationToken)
    {
        try { await _images.DeleteAsync(imageId, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "SocialPostImage {ImageId} silinemedi (yetim kalabilir)", imageId); }
    }
}

/// <summary>PUT gövdesi — alanlar opsiyonel; null = dokunma.</summary>
public record UpdateSocialPostBody(string? Topic, string? CustomImageUrl, List<SocialPostVariantBody>? Variants);

/// <summary>Bir dilin düzenlenen metni.</summary>
public record SocialPostVariantBody(string Language, string CardTitle, string Body, string BodyShort, string? Hashtags);

/// <summary>Ret gövdesi (sebep opsiyonel, ≤ 500).</summary>
public record RejectSocialPostBody(string? Reason);
