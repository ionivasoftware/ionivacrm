using System.Text.RegularExpressions;
using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Liftdesk kurumsal site blog kuyruğu — onay ekranı (docs/crm-site-blog-api.md, Liftdesk deposu). Liftdesk
/// uçlarını proxy'ler; statik CRM anahtarı sunucuda kalır. SuperAdmin'e açık. Yerel ayar listesi Liftdesk'ten
/// gelir (sabit dört dil varsayılmaz). Doğrulayıcı Liftdesk'te çalışır: 400 SITE_BLOG_VALIDATION'ın
/// <c>errors[]</c> listesi ("&lt;locale&gt;:&lt;code&gt;: açıklama") aynen ekrana geçer; <c>message</c> alanında
/// Liftdesk'in <c>errorCode</c>'u taşınır (ekran alan altı uyarı için kodu kullanır).
/// </summary>
[Route("api/v1/site-blog")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SiteBlogController : ApiControllerBase
{
    private static readonly Regex DateRe = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex SlugRe = new(@"^[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly string[] Statuses = { "Pending", "Approved", "Published", "Rejected", "Retired" };
    private static readonly string[] TopicStatuses = { "Open", "Taken", "Done", "Dropped" };

    private readonly ILiftdeskSiteBlogClient _client;
    private readonly ILogger<SiteBlogController> _logger;

    public SiteBlogController(ILiftdeskSiteBlogClient client, ILogger<SiteBlogController> logger)
    {
        _client = client; _logger = logger;
    }

    // ── Yerel ayarlar / liste / sayım ────────────────────────────────────────

    /// <summary>GET /api/v1/site-blog/locales — sekmeler ve süzgeç (sıra = ekran sırası).</summary>
    [HttpGet("locales")]
    public async Task<IActionResult> Locales(CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.GetLocalesAsync(cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yerel ayar listesi alınamadı.");
        return OkResponse(env.Data);
    }

    /// <summary>GET /api/v1/site-blog?status=&amp;locale=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? locale, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        string? normStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normStatus = Statuses.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normStatus is null) return BadRequest(ApiResponse<object>.Fail("status Pending | Approved | Published | Rejected | Retired olmalı.", 400));
        }
        var env = await _client.ListAsync(normStatus, string.IsNullOrWhiteSpace(locale) ? null : locale.Trim(), Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yazı listesi alınamadı.");
        return OkResponse(env.Data);
    }

    /// <summary>GET /api/v1/site-blog/pending-count — menü rozeti (haftalık rutin: Pazartesi taslağını onaylamak).</summary>
    [HttpGet("pending-count")]
    public async Task<IActionResult> PendingCount(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured) return OkResponse(new { count = 0 });
        var env = await _client.ListAsync("Pending", null, 1, 1, cancellationToken);
        return OkResponse(new { count = env.Success && env.Data is not null ? env.Data.TotalCount : 0 });
    }

    /// <summary>GET /api/v1/site-blog/{postId} — tüm varyantlar Markdown dâhil.</summary>
    [HttpGet("{postId:guid}")]
    public async Task<IActionResult> Get(Guid postId, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.GetAsync(postId, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yazı alınamadı.");
        return OkResponse(env.Data);
    }

    // ── Düzenleme / durum ────────────────────────────────────────────────────

    /// <summary>PUT /api/v1/site-blog/variants/{variantId} — Retired hariç her durumda; Published'da slug kilitli.</summary>
    [HttpPut("variants/{variantId:guid}")]
    public async Task<IActionResult> UpdateVariant(Guid variantId, [FromBody] UpdateSiteBlogVariantBody body, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        if (ValidateUpdate(body) is { } err) return BadRequest(ApiResponse<object>.Fail(err, 400));
        var env = await _client.UpdateVariantAsync(variantId, new LiftdeskSiteBlogVariantUpdate(
            body.Title?.Trim(), body.Description?.Trim(), body.Slug?.Trim(), body.BodyMarkdown, body.RelatedPageSlug?.Trim(), body.PublishDate?.Trim()), cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yazı kaydedilemedi.", conflict: "Yayından düşürülmüş varyant düzenlenemez; önce yeniden onaylayın.");
        return OkResponse(env.Data);
    }

    [HttpPost("variants/{variantId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid variantId, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.ApproveVariantAsync(variantId, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Onaylanamadı.", conflict: "Bu varyant bu durumda onaylanamaz (yalnız bekleyen, reddedilen ya da düşürülmüş).");
        _logger.LogInformation("Site blog varyantı onaylandı: {VariantId}", variantId);
        return OkResponse(env.Data);
    }

    [HttpPost("variants/{variantId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid variantId, [FromBody] RejectSiteBlogBody? body, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        if (body?.Reason is { Length: > 500 }) return BadRequest(ApiResponse<object>.Fail("Ret sebebi en fazla 500 karakter olabilir.", 400));
        var env = await _client.RejectVariantAsync(variantId, body?.Reason, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Reddedilemedi.", conflict: "Yayındaki varyant reddedilemez; \"Yayından düşür\" kullanın.");
        return OkResponse(env.Data);
    }

    /// <summary>POST /api/v1/site-blog/variants/{variantId}/retire — Published → Retired (adres sonraki yayında 404 olur).</summary>
    [HttpPost("variants/{variantId:guid}/retire")]
    public async Task<IActionResult> Retire(Guid variantId, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.RetireVariantAsync(variantId, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yayından düşürülemedi.", conflict: "Yalnız yayındaki varyant düşürülebilir.");
        _logger.LogInformation("Site blog varyantı yayından düşürüldü: {VariantId}", variantId);
        return OkResponse(env.Data);
    }

    [HttpPost("{postId:guid}/approve-all")]
    public async Task<IActionResult> ApproveAll(Guid postId, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.ApproveAllAsync(postId, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Toplu onay yapılamadı.");
        return OkResponse(env.Data);
    }

    /// <summary>DELETE /api/v1/site-blog/{postId} — yayınlanmış/düşürülmüş varyant yoksa; ajan aynı konuyu yeniden yazabilir.</summary>
    [HttpDelete("{postId:guid}")]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.DeleteAsync(postId, cancellationToken);
        if (!env.Success) return Fail(env, "Silinemedi.", conflict: "Yayınlanmış ya da düşürülmüş varyantı olan konu silinemez.");
        return OkResponse(new { deleted = true });
    }

    // ── Yayın ────────────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/site-blog/publish — yayın tetiğini şimdi çek; triggered=false sebebi yanıtta.</summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.PublishAsync(cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yayın tetiği çekilemedi.");
        _logger.LogInformation("Site yayın tetiği: triggered={Triggered} reason={Reason} awaiting={Awaiting}", env.Data.Triggered, env.Data.Reason, env.Data.AwaitingPublish);
        return OkResponse(env.Data);
    }

    [HttpGet("publish-status")]
    public async Task<IActionResult> PublishStatus(CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.GetPublishStatusAsync(cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Yayın durumu alınamadı.");
        return OkResponse(env.Data);
    }

    // ── Konu kuyruğu ─────────────────────────────────────────────────────────

    [HttpGet("topic-requests")]
    public async Task<IActionResult> TopicRequests([FromQuery] string? status, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        string? norm = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            norm = TopicStatuses.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (norm is null) return BadRequest(ApiResponse<object>.Fail("status Open | Taken | Done | Dropped olmalı.", 400));
        }
        var env = await _client.GetTopicRequestsAsync(norm, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Konu kuyruğu alınamadı.");
        return OkResponse(env.Data);
    }

    [HttpPost("topic-requests")]
    public async Task<IActionResult> CreateTopicRequest([FromBody] CreateTopicRequestBody body, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var topic = body.Topic?.Trim();
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 300) return BadRequest(ApiResponse<object>.Fail("Konu 1–300 karakter olmalı.", 400));
        if (body.Note is { Length: > 1000 }) return BadRequest(ApiResponse<object>.Fail("Not en fazla 1000 karakter olabilir.", 400));
        var group = string.IsNullOrWhiteSpace(body.Group) ? null : body.Group.Trim();
        if (group is not null && !SlugRe.IsMatch(group)) return BadRequest(ApiResponse<object>.Fail("Grup kebab-case olmalı (a-z, 0-9, -).", 400));
        var env = await _client.CreateTopicRequestAsync(topic, group, string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(), cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Konu kuyruğa eklenemedi.");
        return OkResponse(env.Data);
    }

    [HttpDelete("topic-requests/{id:guid}")]
    public async Task<IActionResult> DropTopicRequest(Guid id, CancellationToken cancellationToken = default)
    {
        if (NotConfigured() is { } nc) return nc;
        var env = await _client.DropTopicRequestAsync(id, cancellationToken);
        if (!env.Success || env.Data is null) return Fail(env, "Konu silinemedi.", conflict: "Alınmış ya da bitmiş konu silinemez.", notFound: "Konu isteği bulunamadı.");
        return OkResponse(env.Data);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private IActionResult? NotConfigured() =>
        _client.IsConfigured ? null : BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

    /// <summary>Liftdesk zarfını operatör diline çevirir; doğrulayıcı hataları (errors[]) ve errorCode (message) aynen geçer.</summary>
    private IActionResult Fail<T>(LiftdeskEnvelope<T> env, string fallback, string? conflict = null, string? notFound = null)
    {
        var code = env.StatusCode is 400 or 401 or 404 or 409 or 503 or 504 ? env.StatusCode : 400;
        if (code == 400 && env.Errors is { Count: > 0 })
        {
            var r = ApiResponse<object>.Fail(env.Errors, 400);
            r.Message = env.ErrorCode ?? "SITE_BLOG_VALIDATION";
            return BadRequest(r);
        }
        var message = code switch
        {
            404 => notFound ?? "Yazı bulunamadı (silinmiş olabilir).",
            409 => conflict ?? "Yazı bu durumda değiştirilemez.",
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 or 504 => env.Message ?? fallback,
            400 => env.ErrorCode == "SITE_BLOG_SLUG_LOCKED" ? "Yayındaki yazının adresi (slug) değiştirilemez." : string.IsNullOrWhiteSpace(env.Message) ? fallback : $"{fallback} ({env.Message})",
            _ => fallback,
        };
        var resp = ApiResponse<object>.Fail(message, code);
        resp.Message = env.ErrorCode;
        return StatusCode(code, resp);
    }

    /// <summary>Ucuz ön kontroller — Liftdesk'in doğrulayıcısına gitmeden Türkçe 400.</summary>
    public static string? ValidateUpdate(UpdateSiteBlogVariantBody body)
    {
        if (body.Title is not null && body.Title.Trim().Length is 0 or > 200) return "Başlık 1–200 karakter olmalı.";
        if (body.Description is not null && body.Description.Trim().Length is < 120 or > 160) return "Açıklama 120–160 karakter olmalı.";
        if (body.Slug is not null && !SlugRe.IsMatch(body.Slug.Trim())) return "Slug yalnız a-z, 0-9 ve - içerebilir.";
        if (body.PublishDate is not null && !DateRe.IsMatch(body.PublishDate.Trim())) return "Yayın tarihi yyyy-MM-dd olmalı.";
        if (body.BodyMarkdown is not null && body.BodyMarkdown.Trim().Length == 0) return "Gövde boş olamaz.";
        if (body.RelatedPageSlug is { Length: > 0 } rel && !SlugRe.IsMatch(rel.Trim())) return "Bağlı sayfa slug'ı yalnız a-z, 0-9 ve - içerebilir.";
        return null;
    }
}

/// <summary>PUT variants/{id} gövdesi — null alan dokunulmaz; relatedPageSlug "" temizler.</summary>
public record UpdateSiteBlogVariantBody(string? Title, string? Description, string? Slug, string? BodyMarkdown, string? RelatedPageSlug, string? PublishDate);

public record RejectSiteBlogBody(string? Reason);

public record CreateTopicRequestBody(string? Topic, string? Group, string? Note);
