using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Leads;
using IonCrm.Application.Features.Outreach;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.API.Controllers;

/// <summary>
/// Global lead queue + outreach (SuperAdmin). Approve → the sequence (intro + follow-ups from marketing/global/sequence.json)
/// is scheduled into the send window; it goes out from the shared company mailbox when one is configured (signed with the
/// approving user's name), otherwise from the approving user's own Gmail.
/// Replies/bounces (IMAP) and unsubscribes stop a sequence; pause/resume/stop/send-now are manual controls.
/// </summary>
[Route("api/v1/global-leads")]
[Authorize(Policy = "SuperAdmin")]
public sealed class GlobalLeadsController : ApiControllerBase
{
    private readonly IGlobalLeadRepository _repo;
    private readonly ILeadCandidateRepository _runs;
    private readonly IOutreachService _outreach;
    private readonly IOutreachState _state;
    private readonly ICurrentUserService _currentUser;
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GlobalLeadsController> _logger;

    public GlobalLeadsController(IGlobalLeadRepository repo, ILeadCandidateRepository runs, IOutreachService outreach, IOutreachState state, ICurrentUserService currentUser, ApplicationDbContext db, IConfiguration configuration, ILogger<GlobalLeadsController> logger)
    {
        _repo = repo; _runs = runs; _outreach = outreach; _state = state; _currentUser = currentUser; _db = db; _configuration = configuration; _logger = logger;
    }

    /// <summary>GET /api/v1/global-leads?status=&amp;outreach=&amp;country=&amp;search=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? outreach, [FromQuery] string? country, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        GlobalLeadStatus? st = null; OutreachStatus? os = null;
        if (!string.IsNullOrWhiteSpace(status)) { if (!Enum.TryParse(status, true, out GlobalLeadStatus p)) return BadRequest(ApiResponse<object>.Fail("status Pending | Approved | Rejected | Duplicate olmalı.", 400)); st = p; }
        if (!string.IsNullOrWhiteSpace(outreach)) { if (!Enum.TryParse(outreach, true, out OutreachStatus p)) return BadRequest(ApiResponse<object>.Fail("outreach geçersiz.", 400)); os = p; }
        var size = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _repo.ListAsync(st, os, country, search, Math.Max(1, page), size, cancellationToken);
        return OkResponse(new { items = items.Select(ToDto).ToList(), totalCount = total, page = Math.Max(1, page), pageSize = size });
    }

    /// <summary>GET /api/v1/global-leads/meta — counts, countries, sequence, materials, sender readiness, last cycle.</summary>
    [HttpGet("meta")]
    public async Task<IActionResult> Meta(CancellationToken cancellationToken = default)
    {
        var byStatus = await _repo.CountsByStatusAsync(cancellationToken);
        var byOutreach = await _repo.CountsByOutreachAsync(cancellationToken);
        var byCountry = await _repo.CountsByCountryAsync(cancellationToken);
        var (steps, attachments, folder) = _outreach.GetMaterials();
        var def = _outreach.GetSequence();
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
        var shared = _outreach.SharedSender;
        var senderReady = shared is not null || (me is not null && !string.IsNullOrWhiteSpace(me.MailAddress) && !string.IsNullOrWhiteSpace(me.MailPasswordEncrypted));
        return OkResponse(new
        {
            counts = new
            {
                pending = byStatus.GetValueOrDefault(GlobalLeadStatus.Pending), approved = byStatus.GetValueOrDefault(GlobalLeadStatus.Approved),
                rejected = byStatus.GetValueOrDefault(GlobalLeadStatus.Rejected), duplicate = byStatus.GetValueOrDefault(GlobalLeadStatus.Duplicate),
            },
            outreach = Enum.GetValues<OutreachStatus>().ToDictionary(k => k.ToString(), k => byOutreach.GetValueOrDefault(k)),
            countries = byCountry.OrderByDescending(kv => kv.Value).Select(kv => new { code = kv.Key, count = kv.Value }).ToList(),
            supportedCountries = GlobalLeadRegions.CountryOrder.ToList(),
            // Ülke kuralları (OutreachPolicy): otomatik e-posta izni, şablon kültürü ve çeviri hazır mı, saat dilimi.
            emailCountries = OutreachPolicy.EmailCountries(_configuration["Outreach:EmailCountries"]).OrderBy(c => c).ToList(),
            catalog = GlobalLeadRegions.CountryOrder.Select(c =>
            {
                var culture = OutreachPolicy.TemplateCulture(c);
                var intro = def.Steps.Count > 0 ? def.Steps[0].Template : "intro.en-GB.html";
                return new
                {
                    code = c, regions = GlobalLeadRegions.ByCountry[c].Length,
                    emailAllowed = OutreachPolicy.EmailAllowed(c, _configuration["Outreach:EmailCountries"]),
                    culture,
                    templatesReady = culture == OutreachPolicy.DefaultCulture || System.IO.File.Exists(Path.Combine(folder, OutreachPolicy.ReCulture(intro, culture))),
                    timeZone = OutreachPolicy.TimeZoneId(c),
                };
            }).ToList(),
            sequence = new
            {
                timeZone = def.TimeZone, startHour = def.StartHour, endHour = def.EndHour, weekdaysOnly = def.WeekdaysOnly,
                steps = steps.Select(s => new { template = s.Template, delayDays = s.DelayDays, attachments = s.Attachments, subjects = s.Subjects, exists = s.Exists }).ToList(),
                attachments, folder,
            },
            limits = new
            {
                dailyCapPerSender = Math.Max(1, _configuration.GetValue("Outreach:DailyCapPerSender", 40)),
                enabled = _configuration.GetValue("Outreach:Enabled", true),
                intervalMinutes = Math.Clamp(_configuration.GetValue("Outreach:IntervalMinutes", 10), 2, 240),
            },
            sender = new
            {
                ready = senderReady, address = shared?.Address ?? me?.MailAddress, name = me is null ? null : (string.IsNullOrWhiteSpace(me.MailSenderName) ? me.FullName : me.MailSenderName),
                shared = shared is not null, source = shared?.Source, inboxCheck = shared?.InboxCheck ?? senderReady,
            },
            agentKeyConfigured = !string.IsNullOrWhiteSpace(_configuration["LeadAgent:ApiKey"]),
            lastRun = _state.LastRun,
        });
    }

    /// <summary>GET /api/v1/global-leads/pending-count — menü rozeti.</summary>
    [HttpGet("pending-count")]
    public async Task<IActionResult> PendingCount(CancellationToken cancellationToken = default)
    {
        var counts = await _repo.CountsByStatusAsync(cancellationToken);
        return OkResponse(new { count = counts.GetValueOrDefault(GlobalLeadStatus.Pending) });
    }

    /// <summary>GET /api/v1/global-leads/runs?limit=20 — global ajan koşuları (Cities "XX:…" olanlar).</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var runs = await _runs.ListRunsAsync(Math.Clamp(limit, 1, 100) * 3, cancellationToken);
        var global = runs.Where(r => r.Cities.Length >= 3 && r.Cities[2] == ':' && char.IsUpper(r.Cities[0]) && char.IsUpper(r.Cities[1])).Take(Math.Clamp(limit, 1, 100));
        return OkResponse(global.Select(r => new { r.RunId, r.StartedAt, r.FinishedAt, country = r.Cities[..2], regions = r.Cities[3..], r.Found, r.Created, r.Existing, r.Invalid, r.Notes }).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        var emails = await _repo.GetEmailsAsync(id, cancellationToken);
        var senderIds = emails.Select(e => e.SenderUserId).Concat(l.SenderUserId is { } s ? new[] { s } : Array.Empty<Guid>()).Distinct().ToList();
        var senders = await _db.Users.AsNoTracking().Where(u => senderIds.Contains(u.Id)).Select(u => new { u.Id, u.MailAddress, u.FirstName, u.LastName }).ToListAsync(cancellationToken);
        string? SenderOf(Guid? uid) => senders.FirstOrDefault(u => u.Id == uid) is { } u ? (u.MailAddress ?? $"{u.FirstName} {u.LastName}".Trim()) : null;
        return OkResponse(new
        {
            lead = ToDto(l), sender = SenderOf(l.SenderUserId),
            emails = emails.Select(e => new { e.Id, e.Step, e.TemplateKey, e.Subject, e.To, e.SentAt, status = e.Status.ToString(), e.Error, sender = SenderOf(e.SenderUserId) }).ToList(),
        });
    }

    /// <summary>PUT /api/v1/global-leads/{id} — contact/e-mail/company edits before (or during) outreach.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGlobalLeadBody body, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (body.CompanyName is { } cn) { cn = cn.Trim(); if (cn.Length is 0 or > 300) return BadRequest(ApiResponse<object>.Fail("Firma adı 1–300 karakter olmalı.", 400)); l.CompanyName = cn; }
        if (body.Email is not null) { var e = body.Email.Trim(); if (e.Length > 0 && !GlobalLeadNormalizer.IsUsableEmail(e)) return BadRequest(ApiResponse<object>.Fail("E-posta geçersiz.", 400)); l.Email = e.Length == 0 ? null : e; }
        if (body.ContactName is not null) l.ContactName = Trunc(body.ContactName, 200);
        if (body.Phone is not null) l.Phone = Trunc(body.Phone, 60);
        if (body.Website is not null) l.Website = Trunc(body.Website, 500);
        if (body.City is not null) l.City = Trunc(body.City, 120);
        if (body.Notes is not null) l.Notes = body.Notes.Trim().Length == 0 ? null : body.Notes.Trim();
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    /// <summary>POST /api/v1/global-leads/{id}/approve — {contactName?, email?, startOutreach=true}: onayla ve (varsayılan) diziyi başlat.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveGlobalLeadBody? body, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.Status == GlobalLeadStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Lead zaten onaylanmış.", 409));
        var err = await ApproveOneAsync(l, body?.ContactName, body?.Email, body?.StartOutreach ?? true, body?.ConsentConfirmed == true, cancellationToken);
        return err is null ? OkResponse(ToDto(l)) : BadRequest(ApiResponse<object>.Fail(err, 400));
    }

    /// <summary>POST /api/v1/global-leads/approve-many — {ids[], startOutreach=true}.</summary>
    [HttpPost("approve-many")]
    public async Task<IActionResult> ApproveMany([FromBody] ApproveManyGlobalBody body, CancellationToken cancellationToken = default)
    {
        var rows = await _repo.GetManyAsync(body.Ids ?? new(), cancellationToken);
        var approved = 0; var withoutMail = 0; var errors = new List<string>();
        var emailCountries = _configuration["Outreach:EmailCountries"];
        foreach (var l in rows.Where(r => r.Status != GlobalLeadStatus.Approved))
        {
            // Toplu onayda rıza teyidi verilemez: e-postası kapalı ülke (Almanya) yalnız onaylanır, dizi başlamaz.
            var wantMail = body.StartOutreach ?? true;
            var mail = wantMail && OutreachPolicy.EmailAllowed(l.Country, emailCountries);
            var err = await ApproveOneAsync(l, null, null, mail, false, cancellationToken);
            if (err is null) { approved++; if (wantMail && !mail) withoutMail++; } else errors.Add($"{l.CompanyName}: {err}");
        }
        return OkResponse(new { approved, withoutMail, errors });
    }

    private async Task<string?> ApproveOneAsync(GlobalLead l, string? contactName, string? email, bool startOutreach, bool consentConfirmed, CancellationToken cancellationToken)
    {
        if (contactName is not null) l.ContactName = Trunc(contactName, 200);
        if (email is not null) { var e = email.Trim(); if (e.Length > 0 && !GlobalLeadNormalizer.IsUsableEmail(e)) return "E-posta geçersiz."; l.Email = e.Length == 0 ? null : e; }
        if (startOutreach)
        {
            if (!GlobalLeadNormalizer.IsUsableEmail(l.Email)) return "Outreach için geçerli bir e-posta adresi gerekli (önce düzenleyin ya da outreach'siz onaylayın).";
            if (ConsentGate(l, consentConfirmed) is { } gate) return gate;
            var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
            if (me is null) return "Oturum kullanıcısı bulunamadı.";
            if (_outreach.SharedSender is null && (string.IsNullOrWhiteSpace(me.MailAddress) || string.IsNullOrWhiteSpace(me.MailPasswordEncrypted)))
                return "Gönderici yok: ortak posta kutusu tanımlı değil ve Gmail ayarlarınız eksik (Ayarlar → E-posta Gönderimi).";
            if (l.OutreachStatus == OutreachStatus.Unsubscribed) return "Bu lead abonelikten çıkmış; outreach başlatılamaz.";
            l.SenderUserId = me.Id; l.OutreachStatus = OutreachStatus.Active; l.OutreachStep = 0; l.LastError = null;
            if (string.IsNullOrWhiteSpace(l.UnsubscribeToken)) l.UnsubscribeToken = OutreachSequence.NewUnsubscribeToken();
            l.NextSendAt = OutreachSequence.NextSendTime(DateTime.UtcNow, _outreach.GetSequenceFor(l.Country));
        }
        l.Status = GlobalLeadStatus.Approved; l.ReviewedAt = DateTime.UtcNow; l.ReviewedBy = _currentUser.Email; l.RejectReason = null;
        await _repo.UpdateAsync(l, cancellationToken);
        _logger.LogInformation("Global lead onaylandı: {Name} ({Country}) outreach={Start}", l.CompanyName, l.Country, startOutreach);
        return null;
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeadBody? body, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.Status == GlobalLeadStatus.Approved && l.OutreachStatus == OutreachStatus.Active) return StatusCode(409, ApiResponse<object>.Fail("Aktif outreach'i olan lead reddedilemez; önce durdurun.", 409));
        l.Status = GlobalLeadStatus.Rejected; l.RejectReason = Trunc(body?.Reason, 500); l.ReviewedAt = DateTime.UtcNow; l.ReviewedBy = _currentUser.Email;
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    /// <summary>POST /api/v1/global-leads/reject-many — {ids[], reason?}.</summary>
    [HttpPost("reject-many")]
    public async Task<IActionResult> RejectMany([FromBody] RejectManyBody body, CancellationToken cancellationToken = default)
    {
        var rows = await _repo.GetManyAsync(body.Ids ?? new(), cancellationToken);
        var n = 0;
        foreach (var l in rows.Where(r => r.Status != GlobalLeadStatus.Approved))
        {
            l.Status = GlobalLeadStatus.Rejected; l.RejectReason = Trunc(body.Reason, 500); l.ReviewedAt = DateTime.UtcNow; l.ReviewedBy = _currentUser.Email;
            await _repo.UpdateAsync(l, cancellationToken); n++;
        }
        return OkResponse(new { rejected = n });
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.Status == GlobalLeadStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Onaylanmış lead geri alınamaz.", 409));
        l.Status = GlobalLeadStatus.Pending; l.RejectReason = null; l.ReviewedAt = null; l.ReviewedBy = null;
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    // ── Outreach controls ────────────────────────────────────────────────────

    /// <summary>POST /api/v1/global-leads/{id}/outreach/start — onaylı ama başlamamış/tamamlanmış/durmuş lead için diziyi (yeniden) başlat.</summary>
    [HttpPost("{id:guid}/outreach/start")]
    public async Task<IActionResult> StartOutreach(Guid id, [FromBody] StartOutreachBody? body, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.Status != GlobalLeadStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Önce lead'i onaylayın.", 409));
        if (l.OutreachStatus == OutreachStatus.Unsubscribed) return StatusCode(409, ApiResponse<object>.Fail("Abonelikten çıkmış lead'e gönderim yapılamaz.", 409));
        if (l.OutreachStatus == OutreachStatus.Active) return StatusCode(409, ApiResponse<object>.Fail("Dizi zaten aktif.", 409));
        if (!GlobalLeadNormalizer.IsUsableEmail(l.Email)) return BadRequest(ApiResponse<object>.Fail("Geçerli e-posta adresi yok.", 400));
        if (ConsentGate(l, body?.ConsentConfirmed == true) is { } gate) return BadRequest(ApiResponse<object>.Fail(gate, 400));
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
        if (me is null) return BadRequest(ApiResponse<object>.Fail("Oturum kullanıcısı bulunamadı.", 400));
        if (_outreach.SharedSender is null && (string.IsNullOrWhiteSpace(me.MailAddress) || string.IsNullOrWhiteSpace(me.MailPasswordEncrypted)))
            return BadRequest(ApiResponse<object>.Fail("Gönderici yok: ortak posta kutusu tanımlı değil ve Gmail ayarlarınız eksik (Ayarlar → E-posta Gönderimi).", 400));
        l.SenderUserId = me.Id; l.OutreachStatus = OutreachStatus.Active; l.LastError = null;
        if (body?.Restart == true) { l.OutreachStep = 0; l.ThreadMessageId = null; }
        l.NextSendAt = OutreachSequence.NextSendTime(DateTime.UtcNow, _outreach.GetSequenceFor(l.Country));
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    [HttpPost("{id:guid}/outreach/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.OutreachStatus != OutreachStatus.Active) return StatusCode(409, ApiResponse<object>.Fail("Yalnız aktif dizi duraklatılır.", 409));
        l.OutreachStatus = OutreachStatus.Paused;
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    [HttpPost("{id:guid}/outreach/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.OutreachStatus is not (OutreachStatus.Paused or OutreachStatus.Failed)) return StatusCode(409, ApiResponse<object>.Fail("Yalnız duraklatılmış/hatalı dizi sürdürülür.", 409));
        l.OutreachStatus = OutreachStatus.Active; l.LastError = null;
        l.NextSendAt = OutreachSequence.NextSendTime(DateTime.UtcNow, _outreach.GetSequence());
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    /// <summary>POST /api/v1/global-leads/{id}/outreach/stop — {reason: replied | unsubscribed | bounced | completed}.</summary>
    [HttpPost("{id:guid}/outreach/stop")]
    public async Task<IActionResult> Stop(Guid id, [FromBody] StopOutreachBody? body, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        var reason = (body?.Reason ?? "completed").Trim().ToLowerInvariant();
        switch (reason)
        {
            case "replied": l.OutreachStatus = OutreachStatus.Replied; l.RepliedAt = DateTime.UtcNow; break;
            case "unsubscribed": l.OutreachStatus = OutreachStatus.Unsubscribed; l.UnsubscribedAt = DateTime.UtcNow; break;
            case "bounced": l.OutreachStatus = OutreachStatus.Bounced; l.BouncedAt = DateTime.UtcNow; break;
            case "completed": l.OutreachStatus = OutreachStatus.Completed; break;
            default: return BadRequest(ApiResponse<object>.Fail("reason replied | unsubscribed | bounced | completed olmalı.", 400));
        }
        l.NextSendAt = null;
        await _repo.UpdateAsync(l, cancellationToken);
        return OkResponse(ToDto(l));
    }

    /// <summary>POST /api/v1/global-leads/{id}/outreach/send-now — sıradaki adımı hemen gönder (pencere/limit beklenmez).</summary>
    [HttpPost("{id:guid}/outreach/send-now")]
    public async Task<IActionResult> SendNow(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        if (l.Status != GlobalLeadStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Önce lead'i onaylayın.", 409));
        if (l.OutreachStatus is OutreachStatus.Unsubscribed or OutreachStatus.Bounced or OutreachStatus.Replied) return StatusCode(409, ApiResponse<object>.Fail($"Dizi {l.OutreachStatus} durumunda; gönderim yapılmaz.", 409));
        if (l.SenderUserId is null) l.SenderUserId = _currentUser.UserId;
        var err = await _outreach.SendNextStepAsync(l, cancellationToken);
        return err is null ? OkResponse(ToDto(l)) : BadRequest(ApiResponse<object>.Fail(err, 400));
    }

    /// <summary>POST /api/v1/global-leads/outreach/run — bir zamanlayıcı döngüsünü şimdi çalıştır.</summary>
    [HttpPost("outreach/run")]
    public async Task<IActionResult> RunCycle(CancellationToken cancellationToken = default) => OkResponse(await _outreach.RunDueAsync(cancellationToken));

    /// <summary>POST /api/v1/global-leads/{id}/outreach/check-inbox — IMAP ile yanıt/bounce kontrolü (elle).</summary>
    [HttpPost("{id:guid}/outreach/check-inbox")]
    public async Task<IActionResult> CheckInbox(Guid id, CancellationToken cancellationToken = default)
    {
        var l = await _repo.GetAsync(id, cancellationToken);
        if (l is null) return NotFound(ApiResponse<object>.Fail("Lead bulunamadı.", 404));
        var check = await _outreach.CheckInboxAsync(l, cancellationToken);
        if (check.Replied) { l.OutreachStatus = OutreachStatus.Replied; l.RepliedAt = DateTime.UtcNow; l.NextSendAt = null; await _repo.UpdateAsync(l, cancellationToken); }
        else if (check.Bounced) { l.OutreachStatus = OutreachStatus.Bounced; l.BouncedAt = DateTime.UtcNow; l.NextSendAt = null; await _repo.UpdateAsync(l, cancellationToken); }
        return OkResponse(new { check.Replied, check.Bounced, check.Detail, lead = ToDto(l) });
    }

    /// <summary>
    /// Ülke e-posta kapısı (<see cref="OutreachPolicy"/>): otomatik soğuk e-posta kapalı ülkede (Almanya — UWG §7, önceden
    /// açık rıza) dizi yalnız operatör rızayı teyit ederse başlar; teyit lead notuna yazılır. İzinli ülkede null.
    /// </summary>
    private string? ConsentGate(GlobalLead l, bool consentConfirmed)
    {
        if (OutreachPolicy.EmailAllowed(l.Country, _configuration["Outreach:EmailCountries"])) return null;
        if (!consentConfirmed)
            return $"{OutreachPolicy.CountryLabelTr(l.Country)} için otomatik tanıtım e-postası kapalı: bu ülkede B2B soğuk e-posta için önceden açık rıza gerekir (Almanya: UWG §7). Rızayı telefonla ya da yazılı aldıysanız 'rıza teyidi' ile başlatın; yoksa e-postasız onaylayıp telefonla ulaşın.";
        var line = $"E-posta rızası teyit edildi ({_currentUser.Email}, {DateTime.UtcNow:yyyy-MM-dd}).";
        l.Notes = string.IsNullOrWhiteSpace(l.Notes) ? line : Trunc($"{l.Notes.TrimEnd()}\n{line}", 2000);
        return null;
    }

    private static string? Trunc(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    internal static GlobalLeadDto ToDto(GlobalLead l) => new(
        l.Id, l.Country, l.Region, l.CompanyName, l.ContactName, l.Email, l.Phone, l.Website, l.Address, l.City, l.Source, l.SourceUrl, l.Snippet, l.Confidence,
        l.Status.ToString(), l.PossibleMatchLeadId, l.MatchNotes, l.AgentRunId, l.ReviewedBy, l.ReviewedAt, l.RejectReason,
        l.OutreachStatus.ToString(), l.OutreachStep, l.NextSendAt, l.LastSentAt, l.SentCount, l.SenderUserId, l.RepliedAt, l.BouncedAt, l.UnsubscribedAt, l.LastError, l.Notes, l.CreatedAt);
}

public record GlobalLeadDto(
    Guid Id, string Country, string? Region, string CompanyName, string? ContactName, string? Email, string? Phone, string? Website, string? Address, string? City,
    string Source, string? SourceUrl, string? Snippet, int Confidence, string Status, Guid? PossibleMatchLeadId, string? MatchNotes, string? AgentRunId,
    string? ReviewedBy, DateTime? ReviewedAt, string? RejectReason, string OutreachStatus, int OutreachStep, DateTime? NextSendAt, DateTime? LastSentAt, int SentCount,
    Guid? SenderUserId, DateTime? RepliedAt, DateTime? BouncedAt, DateTime? UnsubscribedAt, string? LastError, string? Notes, DateTime CreatedAt);

public record UpdateGlobalLeadBody(string? CompanyName, string? ContactName, string? Email, string? Phone, string? Website, string? City, string? Notes);

/// <summary><paramref name="ConsentConfirmed"/>: e-postası kapalı ülkede (Almanya) operatör rızayı telefonla/yazılı aldı — dizi ancak o zaman başlar.</summary>
public record ApproveGlobalLeadBody(string? ContactName, string? Email, bool? StartOutreach, bool? ConsentConfirmed);

public record ApproveManyGlobalBody(List<Guid>? Ids, bool? StartOutreach);

public record StartOutreachBody(bool? Restart, bool? ConsentConfirmed);

public record StopOutreachBody(string? Reason);
