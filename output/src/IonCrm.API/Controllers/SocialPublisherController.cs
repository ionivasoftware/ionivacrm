using IonCrm.API.Common;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Sosyal medya publisher panosu: durum/uyarılar, yayın günlüğü (hangi gönderi, hangi hesap, ne zaman,
/// platform post id), elle çalıştırma ve bloke işlerde insan kararı (yeniden dene / atla). SuperAdmin.
/// </summary>
[Route("api/v1/social-publisher")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SocialPublisherController : ApiControllerBase
{
    private readonly ISocialPublishRepository _publish;
    private readonly ISocialAccountRepository _accounts;
    private readonly ISocialPublishRunner _runner;
    private readonly ISocialPublisherState _state;
    private readonly IMetaGraphClient _meta;
    private readonly IXApiClient _x;
    private readonly ILiftdeskSocialPostsClient _ems;
    private readonly IConfiguration _configuration;

    public SocialPublisherController(
        ISocialPublishRepository publish, ISocialAccountRepository accounts, ISocialPublishRunner runner, ISocialPublisherState state,
        IMetaGraphClient meta, IXApiClient x, ILiftdeskSocialPostsClient ems, IConfiguration configuration)
    {
        _publish = publish; _accounts = accounts; _runner = runner; _state = state; _meta = meta; _x = x; _ems = ems; _configuration = configuration;
    }

    /// <summary>GET /api/v1/social-publisher/status — sayımlar, son koşu, uyarılar.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<SocialPublisherStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
    {
        var counts = await _publish.CountJobsByStatusAsync(cancellationToken);
        var accounts = await _accounts.GetAllAsync(cancellationToken);
        var posts = await _publish.ListPostsAsync(200, cancellationToken);
        var warnings = new List<SocialPublisherWarningDto>();

        if (!_configuration.GetValue("SocialPublisher:Enabled", true))
            warnings.Add(new("error", "Publisher kapalı (SocialPublisher:Enabled=false) — hiçbir gönderi yayınlanmaz."));
        if (!_ems.IsConfigured)
            warnings.Add(new("error", "Liftdesk API anahtarı yok — onaylı gönderi kuyruğu okunamıyor."));
        if (SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k]) is null)
            warnings.Add(new("error", "SocialPosts:PublicApiBaseUrl ayarlı değil — Instagram/Facebook görsel URL'si üretilemez; şablon kartlı gönderiler bloke olur."));
        if (!_meta.IsConfigured) warnings.Add(new("info", "Meta uygulaması yapılandırılmamış (Social:Meta:AppId/AppSecret) — Facebook/Instagram bağlanamaz."));
        if (!_x.IsConfigured) warnings.Add(new("info", "X uygulaması yapılandırılmamış (Social:X:ClientId) — X bağlanamaz."));
        if (string.IsNullOrWhiteSpace(_configuration["Social:TokenKey"]))
            warnings.Add(new("info", "Token şifreleme anahtarı JwtSettings:Secret'tan türetiliyor; ayrı bir Social:TokenKey önerilir (JWT anahtarı değişirse token'lar okunamaz olur)."));

        if (accounts.Count == 0) warnings.Add(new("warning", "Bağlı sosyal hesap yok — Meta ya da X bağlayın."));
        foreach (var a in accounts)
        {
            var who = $"{a.Label} ({a.Provider})";
            if (a.Status == SocialAccountStatus.Revoked) warnings.Add(new("error", $"{who}: token geçersiz — yeniden bağlanmalı. {a.LastError}"));
            else if (a.Status == SocialAccountStatus.Expired) warnings.Add(new("error", $"{who}: token süresi doldu — yeniden bağlanmalı."));
            else if (a.Status == SocialAccountStatus.Error) warnings.Add(new("error", $"{who}: izin/platform hatası — {a.LastError}"));
            else if (a.Status == SocialAccountStatus.Expiring || (a.TokenExpiresAt is { } exp && a.RefreshTokenEncrypted is null && exp <= DateTime.UtcNow.AddDays(7)))
                warnings.Add(new("warning", $"{who}: token {a.TokenExpiresAt:dd.MM.yyyy} tarihinde dolacak — yeniden bağlanın."));
            if (a.IsEnabled && string.IsNullOrWhiteSpace(a.Language)) warnings.Add(new("warning", $"{who}: dil eşlemesi yok — bu hesaba hiç gönderi gitmez."));
            if (!a.IsEnabled) warnings.Add(new("info", $"{who}: devre dışı."));
        }

        var blocked = counts.GetValueOrDefault(SocialPublishJobStatus.Blocked);
        if (blocked > 0) warnings.Add(new("error", $"{blocked} yayın işi bloke — Yayın günlüğünden 'Yeniden dene' ya da 'Atla'."));
        foreach (var p in posts.Where(p => p.StampedAt is null && (p.Note is not null || p.StampError is not null)).Take(10))
            warnings.Add(new("warning", $"{p.ScheduledFor:dd.MM.yyyy} · {p.Topic}: {p.Note ?? p.StampError}"));

        return OkResponse(new SocialPublisherStatusDto(
            Enabled: _configuration.GetValue("SocialPublisher:Enabled", true),
            PublishHourLocal: _configuration.GetValue("SocialPublisher:PublishHourLocal", 10),
            TimeZone: _configuration["SocialPublisher:TimeZone"] ?? "Europe/Istanbul",
            MaxLateDays: _configuration.GetValue("SocialPublisher:MaxLateDays", 2),
            IntervalMinutes: _configuration.GetValue("SocialPublisher:IntervalMinutes", 30),
            Counts: new SocialPublishCountsDto(
                counts.GetValueOrDefault(SocialPublishJobStatus.Pending), counts.GetValueOrDefault(SocialPublishJobStatus.InProgress),
                counts.GetValueOrDefault(SocialPublishJobStatus.Succeeded), counts.GetValueOrDefault(SocialPublishJobStatus.Failed),
                blocked, counts.GetValueOrDefault(SocialPublishJobStatus.Skipped),
                posts.Count(p => p.StampedAt is null && p.Note is null)),
            LastRun: _state.LastRun,
            Warnings: warnings));
    }

    /// <summary>GET /api/v1/social-publisher/jobs?postId=&amp;status=&amp;limit= — yayın günlüğü.</summary>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(ApiResponse<List<SocialPublishJobDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Jobs([FromQuery] Guid? postId, [FromQuery] string? status, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        SocialPublishJobStatus? st = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SocialPublishJobStatus>(status, true, out var parsed))
                return BadRequest(ApiResponse<object>.Fail("status Pending | InProgress | Succeeded | Failed | Blocked | Skipped olmalı.", 400));
            st = parsed;
        }
        var jobs = await _publish.ListJobsAsync(postId, st, Math.Clamp(limit, 1, 500), cancellationToken);
        var accounts = (await _accounts.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id);
        var posts = (await _publish.ListPostsAsync(500, cancellationToken)).ToDictionary(p => p.PostId);
        return OkResponse(jobs.Select(j => ToDto(j, accounts.GetValueOrDefault(j.AccountId), posts.GetValueOrDefault(j.PostId))).ToList());
    }

    /// <summary>GET /api/v1/social-publisher/posts?limit= — gönderi bazlı özet (damga durumu).</summary>
    [HttpGet("posts")]
    [ProducesResponseType(typeof(ApiResponse<List<SocialPublishPostDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Posts([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var posts = await _publish.ListPostsAsync(Math.Clamp(limit, 1, 500), cancellationToken);
        var jobs = await _publish.ListJobsAsync(null, null, 2000, cancellationToken);
        var byPost = jobs.GroupBy(j => j.PostId).ToDictionary(g => g.Key, g => g.ToList());
        return OkResponse(posts.Select(p =>
        {
            var pj = byPost.GetValueOrDefault(p.PostId) ?? new();
            return new SocialPublishPostDto(p.PostId, p.ScheduledFor, p.Topic, p.Kind, p.FirstSeenAt, p.AllDoneAt, p.StampedAt, p.StampError, p.Note,
                pj.Count, pj.Count(j => j.Status == SocialPublishJobStatus.Succeeded), pj.Count(j => j.Status is SocialPublishJobStatus.Failed or SocialPublishJobStatus.Blocked));
        }).ToList());
    }

    /// <summary>POST /api/v1/social-publisher/run — bir döngüyü hemen koştur (normal kurallarla).</summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(ApiResponse<SocialPublishRunSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(CancellationToken cancellationToken = default)
    {
        var summary = await _runner.RunOnceAsync(cancellationToken);
        return OkResponse(summary);
    }

    /// <summary>POST /api/v1/social-publisher/jobs/{id}/retry — Failed/Blocked işi hemen yeniden kuyruğa al.</summary>
    [HttpPost("jobs/{id:guid}/retry")]
    [ProducesResponseType(typeof(ApiResponse<SocialPublishJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _publish.GetJobAsync(id, cancellationToken);
        if (job is null) return NotFound(ApiResponse<object>.Fail("İş bulunamadı.", 404));
        if (job.Status is not (SocialPublishJobStatus.Failed or SocialPublishJobStatus.Blocked or SocialPublishJobStatus.Skipped))
            return StatusCode(409, ApiResponse<object>.Fail("Yalnız başarısız, bloke ya da atlanmış iş yeniden denenebilir.", 409));
        job.Status = SocialPublishJobStatus.Pending; job.NextAttemptAt = null; job.FinishedAt = null;
        if (job.Attempts >= Application.Features.SocialPublishing.SocialPublishPlanner.MaxAttempts) job.Attempts = 0; // tavana takılmışsa sayaç sıfırlanır
        await _publish.UpdateJobAsync(job, cancellationToken);
        await ClearStampBlockersAsync(job.PostId, cancellationToken);
        return OkResponse(ToDto(job, await _accounts.GetAsync(job.AccountId, cancellationToken), await _publish.GetPostAsync(job.PostId, cancellationToken)));
    }

    /// <summary>POST /api/v1/social-publisher/jobs/{id}/skip — bu platformda yayınlanmasın; damgayı engellemesin.</summary>
    [HttpPost("jobs/{id:guid}/skip")]
    [ProducesResponseType(typeof(ApiResponse<SocialPublishJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _publish.GetJobAsync(id, cancellationToken);
        if (job is null) return NotFound(ApiResponse<object>.Fail("İş bulunamadı.", 404));
        if (job.Status is SocialPublishJobStatus.Succeeded or SocialPublishJobStatus.InProgress)
            return StatusCode(409, ApiResponse<object>.Fail("Başarılı ya da sürmekte olan iş atlanamaz.", 409));
        job.Status = SocialPublishJobStatus.Skipped; job.FinishedAt = DateTime.UtcNow; job.NextAttemptAt = null;
        await _publish.UpdateJobAsync(job, cancellationToken);
        return OkResponse(ToDto(job, await _accounts.GetAsync(job.AccountId, cancellationToken), await _publish.GetPostAsync(job.PostId, cancellationToken)));
    }

    private async Task ClearStampBlockersAsync(Guid postId, CancellationToken ct)
    {
        var post = await _publish.GetPostAsync(postId, ct);
        if (post is null || post.StampError is null) return;
        post.StampError = null;
        await _publish.UpdatePostAsync(post, ct);
    }

    private static SocialPublishJobDto ToDto(SocialPublishJob j, SocialAccount? a, SocialPublishPost? p) => new(
        j.Id, j.PostId, p?.Topic, p?.ScheduledFor, p?.Kind, j.AccountId, a?.Label ?? "(kaldırılmış hesap)", j.Provider.ToString(), a?.ExternalName,
        j.Language, j.Status.ToString(), j.Attempts, j.NextAttemptAt, j.StartedAt, j.FinishedAt, j.LastError, j.PlatformPostId, j.ImageUrl, j.CreatedAt, j.UpdatedAt);
}

public record SocialPublisherWarningDto(string Level, string Message);

public record SocialPublishCountsDto(int Pending, int InProgress, int Succeeded, int Failed, int Blocked, int Skipped, int AwaitingStamp);

public record SocialPublisherStatusDto(
    bool Enabled, int PublishHourLocal, string TimeZone, int MaxLateDays, int IntervalMinutes,
    SocialPublishCountsDto Counts, SocialPublishRunSummary? LastRun, List<SocialPublisherWarningDto> Warnings);

public record SocialPublishJobDto(
    Guid Id, Guid PostId, string? Topic, DateTime? ScheduledFor, string? Kind, Guid AccountId, string AccountLabel, string Provider, string? AccountName,
    string Language, string Status, int Attempts, DateTime? NextAttemptAt, DateTime? StartedAt, DateTime? FinishedAt, string? LastError,
    string? PlatformPostId, string? ImageUrl, DateTime CreatedAt, DateTime UpdatedAt);

public record SocialPublishPostDto(
    Guid PostId, DateTime ScheduledFor, string Topic, string Kind, DateTime FirstSeenAt, DateTime? AllDoneAt, DateTime? StampedAt, string? StampError, string? Note,
    int JobCount, int SucceededCount, int ProblemCount);
