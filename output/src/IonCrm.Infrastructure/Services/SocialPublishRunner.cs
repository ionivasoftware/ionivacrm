using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// One publisher cycle (contract: "Sosyal Medya Publisher — ION CRM Görev Dokümanı"):
///   1. pull Approved posts from EMS whose day has come (hour policy here, EMS holds only the day);
///   2. create one job per (post × mapped account) — the unique index makes duplicates impossible;
///   3. run due jobs: resolve image (custom URL or template card stored publicly for Instagram),
///      publish, record the platform post id; transient errors back off, permanent ones block;
///   4. stamp EMS <c>POST {id}/published</c> once every job is Succeeded/Skipped (idempotent).
/// Scoped service; the hosted timer and the "run now" endpoint both call it. A Postgres advisory lock
/// keeps rolling-deploy containers from running two cycles at once.
/// </summary>
public sealed class SocialPublishRunner : ISocialPublishRunner
{
    private const long AdvisoryLockKey = 8_120_334_908_117_552L;
    private const int PageSize = 100;
    private static readonly TimeSpan StaleInProgress = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _db;
    private readonly ISocialAccountRepository _accounts;
    private readonly ISocialPublishRepository _publish;
    private readonly ISocialPostImageRepository _images;
    private readonly ILiftdeskSocialPostsClient _ems;
    private readonly IMetaGraphClient _meta;
    private readonly IXApiClient _x;
    private readonly ITokenProtector _protector;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly INotificationEmailSender _mail;
    private readonly ISocialPublisherState _state;
    private readonly ILogger<SocialPublishRunner> _logger;

    public SocialPublishRunner(
        ApplicationDbContext db,
        ISocialAccountRepository accounts,
        ISocialPublishRepository publish,
        ISocialPostImageRepository images,
        ILiftdeskSocialPostsClient ems,
        IMetaGraphClient meta,
        IXApiClient x,
        ITokenProtector protector,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        INotificationEmailSender mail,
        ISocialPublisherState state,
        ILogger<SocialPublishRunner> logger)
    {
        _db = db; _accounts = accounts; _publish = publish; _images = images; _ems = ems; _meta = meta; _x = x;
        _protector = protector; _httpFactory = httpFactory; _configuration = configuration; _mail = mail; _state = state; _logger = logger;
    }

    public async Task<SocialPublishRunSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var notes = new List<string>();
        int postsSeen = 0, created = 0, succeeded = 0, failed = 0, blocked = 0, stamped = 0;
        string? error = null;
        var ran = false;

        try
        {
            if (!_configuration.GetValue("SocialPublisher:Enabled", true)) { notes.Add("SocialPublisher:Enabled=false — kapalı."); return Finish(); }
            if (!_ems.IsConfigured) { notes.Add("Liftdesk API anahtarı yok — EMS kuyruğu okunamıyor."); return Finish(); }
            if (!await TryAdvisoryLockAsync(cancellationToken)) { notes.Add("Başka bir örnek çalışıyor (advisory lock dolu)."); return Finish(); }
            ran = true;

            var tz = SocialPublishPlanner.ResolveTimeZone(_configuration["SocialPublisher:TimeZone"]);
            var publishHour = _configuration.GetValue("SocialPublisher:PublishHourLocal", SocialPublishPlanner.DefaultPublishHour);
            var maxLateDays = _configuration.GetValue("SocialPublisher:MaxLateDays", SocialPublishPlanner.DefaultMaxLateDays);
            var now = DateTime.UtcNow;

            var allAccounts = await _accounts.GetAllAsync(cancellationToken);
            var usable = allAccounts.Where(IsUsable).ToList();
            if (usable.Count == 0) notes.Add("Kullanılabilir (etkin + dil eşlenmiş + sağlıklı) hesap yok.");

            // ── 1. Discover due approved posts ────────────────────────────────
            var todayLocal = DateOnly.FromDateTime(SocialPublishPlanner.ToLocal(now, tz));
            var from = todayLocal.AddDays(-(maxLateDays + 7)).ToString("yyyy-MM-dd"); // a week of slack so stale ones still get a visible note
            var to = todayLocal.ToString("yyyy-MM-dd");
            var approved = await FetchApprovedAsync(from, to, cancellationToken);
            var byId = approved.ToDictionary(p => p.Id);

            foreach (var post in approved)
            {
                if (!SocialPublishPlanner.IsDue(post.ScheduledFor, now, tz, publishHour)) continue;
                postsSeen++;

                var row = await _publish.GetOrAddPostAsync(new SocialPublishPost
                {
                    PostId = post.Id, ScheduledFor = post.ScheduledFor, Topic = post.Topic, Kind = post.Kind, FirstSeenAt = now,
                }, cancellationToken);
                var existingJobs = await _publish.GetJobsForPostAsync(post.Id, cancellationToken);

                if (existingJobs.Count == 0 && SocialPublishPlanner.IsStale(post.ScheduledFor, now, tz, maxLateDays))
                {
                    var note = $"Yayın günü {maxLateDays} günden fazla geçti — yayınlanmadı (bayat). Gerekirse EMS'te reddedin ya da yeniden planlayın.";
                    if (row.Note != note) { row.Note = note; await _publish.UpdatePostAsync(row, cancellationToken); notes.Add($"Bayat: {post.Topic}"); }
                    continue;
                }

                var languages = (post.Variants ?? new()).Select(v => v.Language).ToList();
                var plan = SocialPublishPlanner.PlanJobs(
                    usable.Select(a => (a.Id, a.Provider, a.Language, true)), languages).ToList();

                var createdHere = 0;
                foreach (var (accountId, provider, language) in plan)
                {
                    if (existingJobs.Any(j => j.AccountId == accountId)) continue;
                    var added = await _publish.TryAddJobAsync(new SocialPublishJob
                    {
                        PostId = post.Id, AccountId = accountId, Provider = provider, Language = language,
                        Status = SocialPublishJobStatus.Pending,
                    }, cancellationToken);
                    if (added) createdHere++;
                }
                created += createdHere;

                if (existingJobs.Count == 0 && createdHere == 0)
                {
                    var note = $"Eşlenmiş hesap yok (varyantlar: {string.Join(", ", languages)}) — yayınlanmadı. Hesaplar ekranında dil eşlemesi yapın.";
                    if (row.Note != note) { row.Note = note; await _publish.UpdatePostAsync(row, cancellationToken); }
                }
                else if (row.Note is not null)
                {
                    row.Note = null; await _publish.UpdatePostAsync(row, cancellationToken);
                }
            }

            // ── 2. Run due jobs ───────────────────────────────────────────────
            var due = await _publish.GetDueJobsAsync(now, now - StaleInProgress, cancellationToken);
            foreach (var job in due)
            {
                var result = await RunJobAsync(job, allAccounts, byId, cancellationToken);
                switch (result)
                {
                    case SocialPublishJobStatus.Succeeded: succeeded++; break;
                    case SocialPublishJobStatus.Failed: failed++; break;
                    case SocialPublishJobStatus.Blocked: blocked++; break;
                }
            }

            // ── 3. Stamp back ─────────────────────────────────────────────────
            foreach (var row in await _publish.GetUnstampedPostsAsync(cancellationToken))
            {
                var jobs = await _publish.GetJobsForPostAsync(row.PostId, cancellationToken);
                if (!SocialPublishPlanner.ReadyToStamp(jobs.Select(j => j.Status).ToList())) continue;

                row.AllDoneAt ??= jobs.Where(j => j.FinishedAt is not null).Max(j => j.FinishedAt) ?? now;
                var env = await _ems.MarkPublishedAsync(row.PostId, row.AllDoneAt, cancellationToken);
                row.StampAttempts++;
                if (env.Success)
                {
                    row.StampedAt = DateTime.UtcNow; row.StampError = null; stamped++;
                    _logger.LogInformation("Sosyal gönderi EMS'te Published damgalandı: {PostId} ({Topic})", row.PostId, row.Topic);
                }
                else
                {
                    row.StampError = env.StatusCode == 404
                        ? "EMS'te kayıt bulunamadı (silinmiş olabilir) — damga atılamadı."
                        : env.Message ?? $"EMS damgası reddedildi (HTTP {env.StatusCode}).";
                    _logger.LogWarning("EMS Published damgası başarısız: {PostId} — {Error}", row.PostId, row.StampError);
                }
                await _publish.UpdatePostAsync(row, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "SocialPublisher: döngü hatası");
        }
        return Finish();

        SocialPublishRunSummary Finish()
        {
            var summary = new SocialPublishRunSummary(started, DateTime.UtcNow, ran, postsSeen, created, succeeded, failed, blocked, stamped, notes, error);
            _state.LastRun = summary;
            if (ran)
                _logger.LogInformation("SocialPublisher: gönderi={Posts} yeni iş={Created} başarılı={Ok} geçici hata={Failed} bloke={Blocked} damga={Stamped}",
                    postsSeen, created, succeeded, failed, blocked, stamped);
            return summary;
        }
    }

    private static bool IsUsable(SocialAccount a) =>
        a.IsEnabled && !string.IsNullOrWhiteSpace(a.Language) &&
        a.Status is SocialAccountStatus.Active or SocialAccountStatus.Expiring;

    private async Task<List<LiftdeskSocialPost>> FetchApprovedAsync(string from, string to, CancellationToken ct)
    {
        var all = new List<LiftdeskSocialPost>();
        for (var page = 1; page <= 20; page++)
        {
            var env = await _ems.ListAsync("Approved", from, to, page, PageSize, ct);
            if (!env.Success || env.Data is null)
                throw new InvalidOperationException($"EMS onaylı gönderi listesi alınamadı: {env.Message ?? $"HTTP {env.StatusCode}"}");
            var items = env.Data.Items ?? new();
            all.AddRange(items);
            if (items.Count < PageSize) break;
        }
        return all;
    }

    /// <summary>Runs one job end-to-end and persists its new state; returns the resulting status.</summary>
    private async Task<SocialPublishJobStatus> RunJobAsync(
        SocialPublishJob job, List<SocialAccount> accounts, Dictionary<Guid, LiftdeskSocialPost> approved, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (job.Status == SocialPublishJobStatus.InProgress)
        {
            // A previous attempt died mid-call (deploy/restart). We cannot know whether the platform
            // accepted it — never retry blindly (duplicate post risk); hand it to a human.
            job.Status = SocialPublishJobStatus.Blocked;
            job.LastError = "Önceki deneme yarıda kesildi (2 saatten uzun süredir 'sürüyor'). Platformda gönderi oluşmuş olabilir — kontrol edip 'Yeniden dene' ya da 'Atla' seçin.";
            job.FinishedAt = now;
            await _publish.UpdateJobAsync(job, ct);
            await NotifyAsync("Sosyal medya yayını yarıda kesildi", $"İş {job.Id} ({job.Provider}, {job.Language}) belirsiz durumda. {job.LastError}", ct);
            return job.Status;
        }

        var account = accounts.FirstOrDefault(a => a.Id == job.AccountId);
        if (account is null)
        {
            job.Status = SocialPublishJobStatus.Skipped; job.LastError = "Hesap bağlantısı kaldırılmış — atlandı."; job.FinishedAt = now;
            await _publish.UpdateJobAsync(job, ct); return job.Status;
        }
        if (!IsUsable(account))
        {
            // Not an attempt: wait for the account to be fixed/re-enabled, check again next cycle.
            job.LastError = $"Hesap kullanılamıyor ({(account.IsEnabled ? account.Status.ToString() : "devre dışı")}) — bekliyor.";
            job.NextAttemptAt = now.AddMinutes(30);
            if (job.Status == SocialPublishJobStatus.Pending) job.Status = SocialPublishJobStatus.Failed;
            await _publish.UpdateJobAsync(job, ct); return job.Status;
        }

        var post = approved.GetValueOrDefault(job.PostId) ?? await FetchPostAsync(job.PostId, ct);
        if (post is null || post.Status is not ("Approved" or "Published"))
        {
            job.Status = SocialPublishJobStatus.Skipped; job.FinishedAt = now;
            job.LastError = post is null ? "EMS kaydı bulunamadı (silinmiş) — atlandı." : $"EMS durumu {post.Status} — onay geri alınmış, atlandı.";
            await _publish.UpdateJobAsync(job, ct); return job.Status;
        }
        var variant = post.Variants?.FirstOrDefault(v => string.Equals(v.Language, job.Language, StringComparison.OrdinalIgnoreCase));
        if (variant is null)
        {
            job.Status = SocialPublishJobStatus.Blocked; job.FinishedAt = now; job.LastError = $"Gönderide {job.Language} varyantı yok.";
            await _publish.UpdateJobAsync(job, ct); return job.Status;
        }

        // Mark InProgress BEFORE the platform call so a crash leaves a visible, non-retrying trace.
        job.Status = SocialPublishJobStatus.InProgress; job.StartedAt = now; job.Attempts++; job.LastError = null;
        await _publish.UpdateJobAsync(job, ct);

        try
        {
            var imageUrl = await ResolveImageUrlAsync(post, job.Language, ct);
            job.ImageUrl = imageUrl;
            var caption = SocialPublishPlanner.ComposeCaption(variant.Body, variant.Hashtags);

            string platformId;
            switch (account.Provider)
            {
                case SocialProvider.Facebook:
                    platformId = await _meta.PublishFacebookPhotoAsync(account.ExternalId, _protector.Unprotect(account.AccessTokenEncrypted), imageUrl, caption, ct);
                    break;
                case SocialProvider.Instagram:
                    platformId = await _meta.PublishInstagramImageAsync(account.ExternalId, _protector.Unprotect(account.AccessTokenEncrypted), imageUrl, caption, ct);
                    break;
                case SocialProvider.X:
                {
                    var token = await EnsureXTokenAsync(account, ct);
                    var (bytes, contentType) = await LoadImageBytesAsync(imageUrl, ct);
                    var mediaId = await _x.UploadImageAsync(token, bytes, contentType, ct);
                    platformId = await _x.CreatePostAsync(token, variant.BodyShort, mediaId, ct);
                    break;
                }
                default:
                    throw new SocialPublishException($"Bilinmeyen platform {account.Provider}.", isPermanent: true);
            }

            job.Status = SocialPublishJobStatus.Succeeded; job.PlatformPostId = platformId; job.FinishedAt = DateTime.UtcNow; job.LastError = null;
            _logger.LogInformation("Sosyal gönderi yayınlandı: {Provider} {Account} ← {PostId}/{Lang} → {PlatformId}",
                account.Provider, account.Label, job.PostId, job.Language, platformId);
        }
        catch (SocialPublishException ex)
        {
            await ApplyFailureAsync(job, account, ex.Message, ex.IsPermanent, ex.InvalidatesToken, ex.IsAccountFault, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SocialPublisher: iş {JobId} beklenmedik hata", job.Id);
            await ApplyFailureAsync(job, account, $"Beklenmedik hata: {ex.Message}", isPermanent: false, invalidatesToken: false, accountFault: false, ct);
        }

        await _publish.UpdateJobAsync(job, ct);
        return job.Status;
    }

    private async Task ApplyFailureAsync(SocialPublishJob job, SocialAccount account, string message,
        bool isPermanent, bool invalidatesToken, bool accountFault, CancellationToken ct)
    {
        job.LastError = message;
        if (isPermanent || job.Attempts >= SocialPublishPlanner.MaxAttempts)
        {
            job.Status = SocialPublishJobStatus.Blocked; job.FinishedAt = DateTime.UtcNow; job.NextAttemptAt = null;
            await NotifyAsync($"Sosyal medya yayını bloke: {account.Label}",
                $"<p><b>{account.Provider}</b> hesabı <b>{account.Label}</b> için yayın kalıcı olarak başarısız.</p><p>{System.Net.WebUtility.HtmlEncode(message)}</p><p>CRM → Sosyal Medya → Yayınlama ekranından 'Yeniden dene' ya da 'Atla'.</p>", ct);
        }
        else
        {
            job.Status = SocialPublishJobStatus.Failed;
            job.NextAttemptAt = DateTime.UtcNow + SocialPublishPlanner.Backoff(job.Attempts);
        }

        if (invalidatesToken || accountFault)
        {
            var newStatus = invalidatesToken ? SocialAccountStatus.Revoked : SocialAccountStatus.Error;
            if (account.Status != newStatus)
            {
                account.Status = newStatus; account.LastError = message;
                await _accounts.UpdateAsync(account, ct);
                await NotifyAsync($"Sosyal medya hesabı {(invalidatesToken ? "yeniden bağlanmalı" : "izin hatası")}: {account.Label}",
                    $"<p><b>{account.Provider}</b> — <b>{account.Label}</b>: {System.Net.WebUtility.HtmlEncode(message)}</p><p>{(invalidatesToken ? "Token geçersiz; CRM → Sosyal Medya → Yayınlama ekranından hesabı yeniden bağlayın." : "İzinleri kontrol edin ve 'Doğrula' ile yeniden deneyin.")}</p>", ct);
            }
        }
    }

    private async Task<LiftdeskSocialPost?> FetchPostAsync(Guid postId, CancellationToken ct)
    {
        var env = await _ems.GetAsync(postId, ct);
        if (env.Success) return env.Data;
        if (env.StatusCode == 404) return null;
        throw new InvalidOperationException($"EMS gönderi okunamadı: {env.Message ?? $"HTTP {env.StatusCode}"}");
    }

    /// <summary>Custom image wins; otherwise the EMS template card is downloaded once per (post, language) and stored under our public image URL.</summary>
    private async Task<string> ResolveImageUrlAsync(LiftdeskSocialPost post, string language, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(post.CustomImageUrl)) return post.CustomImageUrl;

        var baseUrl = SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k])
            ?? throw new SocialPublishException("SocialPosts:PublicApiBaseUrl ayarlı değil — Instagram/Facebook görseli herkese açık bir URL ister.", isPermanent: true);

        var existing = await _images.FindCardAsync(post.Id, language, ct);
        if (existing is not null) return SocialPostImageLinks.Build(baseUrl, existing.Id);

        var card = await _ems.GetCardAsync(post.Id, language, ct);
        if (!card.Success || card.Data is null)
            throw new SocialPublishException($"Şablon kart alınamadı ({language}): {card.Message ?? $"HTTP {card.StatusCode}"}", isPermanent: card.StatusCode == 404);

        var image = new SocialPostImage
        {
            PostId = post.Id, Kind = "Card", Language = language, FileName = $"card-{language}.png",
            ContentType = string.IsNullOrWhiteSpace(card.Data.ContentType) ? "image/png" : card.Data.ContentType, Content = card.Data.Content,
        };
        await _images.AddAsync(image, ct);
        return SocialPostImageLinks.Build(baseUrl, image.Id);
    }

    /// <summary>X needs bytes: our own stored image is read from the DB, anything else is downloaded.</summary>
    private async Task<(byte[] Bytes, string ContentType)> LoadImageBytesAsync(string imageUrl, CancellationToken ct)
    {
        if (SocialPostImageLinks.TryParseImageId(imageUrl) is { } ownId)
        {
            var own = await _images.GetAsync(ownId, ct)
                ?? throw new SocialPublishException("Görsel CRM deposunda bulunamadı.", isPermanent: true);
            return (own.Content, own.ContentType);
        }

        var http = _httpFactory.CreateClient("social-image-download");
        HttpResponseMessage response;
        try { response = await http.GetAsync(imageUrl, ct); }
        catch (HttpRequestException ex) { throw new SocialPublishException($"Görsel indirilemedi: {ex.Message}", isPermanent: false, inner: ex); }
        if (!response.IsSuccessStatusCode)
            throw new SocialPublishException($"Görsel indirilemedi (HTTP {(int)response.StatusCode}).", isPermanent: (int)response.StatusCode is 404 or 410);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var type = SocialPostImageLinks.SniffImageType(bytes)
            ?? throw new SocialPublishException("Görsel PNG/JPEG/WebP değil.", isPermanent: true);
        return (bytes, type);
    }

    /// <summary>X access tokens last ~2 h; refresh (rotating refresh token) when within 5 minutes of expiry and persist both.</summary>
    private async Task<string> EnsureXTokenAsync(SocialAccount account, CancellationToken ct)
    {
        var access = _protector.Unprotect(account.AccessTokenEncrypted);
        var needsRefresh = account.TokenExpiresAt is { } exp && exp <= DateTime.UtcNow.AddMinutes(5);
        if (!needsRefresh) return access;
        if (string.IsNullOrEmpty(account.RefreshTokenEncrypted))
            throw new SocialPublishException("X erişim token'ı doldu ve yenileme token'ı yok — yeniden bağlanın.", isPermanent: true, invalidatesToken: true);

        var refreshed = await _x.RefreshAsync(_protector.Unprotect(account.RefreshTokenEncrypted), ct);
        account.AccessTokenEncrypted = _protector.Protect(refreshed.AccessToken);
        if (!string.IsNullOrEmpty(refreshed.RefreshToken)) account.RefreshTokenEncrypted = _protector.Protect(refreshed.RefreshToken);
        account.TokenExpiresAt = refreshed.ExpiresAt;
        account.Status = SocialAccountStatus.Active; account.LastError = null;
        await _accounts.UpdateAsync(account, ct);
        return refreshed.AccessToken;
    }

    private async Task NotifyAsync(string subject, string html, CancellationToken ct)
    {
        try { await _mail.SendAsync($"[ION CRM] {subject}", html, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "SocialPublisher: bildirim e-postası gönderilemedi"); }
    }

    private async Task<bool> TryAdvisoryLockAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_try_advisory_lock({AdvisoryLockKey})";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }
}

/// <summary>Singleton holder for the last cycle summary.</summary>
public sealed class SocialPublisherState : ISocialPublisherState
{
    public SocialPublishRunSummary? LastRun { get; set; }
}
