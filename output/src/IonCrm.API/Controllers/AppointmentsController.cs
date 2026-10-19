using System.Globalization;
using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.CallPlan;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Görüşme Takvimi: randevular (elle + CSO planı), randevudan görüşme kaydı girme, planlayıcı durumu.
/// Tenant kapsamı DbContext filtresinden gelir (ContactHistory ile aynı kural). Yetki: oturum açmış
/// her kullanıcı okur/yazar; planlayıcıyı elle çalıştırmak SuperAdmin ya da projede ProjectAdmin /
/// SalesManager ister.
/// </summary>
[Route("api/v1/appointments")]
[Authorize]
public sealed class AppointmentsController : ApiControllerBase
{
    private readonly IAppointmentRepository _appointments;
    private readonly IContactHistoryRepository _contacts;
    private readonly ICustomerRepository _customers;
    private readonly ICallPlanService _plan;
    private readonly ICurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(
        IAppointmentRepository appointments, IContactHistoryRepository contacts, ICustomerRepository customers, ICallPlanService plan,
        ICurrentUserService currentUser, IConfiguration configuration, ILogger<AppointmentsController> logger)
    {
        _appointments = appointments; _contacts = contacts; _customers = customers; _plan = plan;
        _currentUser = currentUser; _configuration = configuration; _logger = logger;
    }

    private TimeZoneInfo Tz => SocialPublishPlanner.ResolveTimeZone(_configuration["CallPlan:TimeZone"]);

    // ── Liste ────────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/appointments?from=yyyy-MM-dd&amp;to=yyyy-MM-dd (yerel günler, dâhil) &amp;projectId&amp;customerId&amp;assignedUserId&amp;status&amp;kind</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AppointmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid? projectId, [FromQuery] Guid? customerId,
        [FromQuery] Guid? assignedUserId, [FromQuery] string? status, [FromQuery] string? kind, CancellationToken cancellationToken = default)
    {
        var tz = Tz;
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        if (!TryDay(from, todayLocal, out var fromDay)) return BadRequest(ApiResponse<object>.Fail("from yyyy-MM-dd olmalı.", 400));
        if (!TryDay(to, fromDay.AddDays(6), out var toDay)) return BadRequest(ApiResponse<object>.Fail("to yyyy-MM-dd olmalı.", 400));
        if (toDay < fromDay || toDay.DayNumber - fromDay.DayNumber > 62) return BadRequest(ApiResponse<object>.Fail("Aralık en fazla 62 gün olabilir.", 400));
        AppointmentStatus? st = null; AppointmentKind? kd = null;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, true, out AppointmentStatus s0)) return BadRequest(ApiResponse<object>.Fail("status geçersiz.", 400)); else if (!string.IsNullOrWhiteSpace(status)) st = Enum.Parse<AppointmentStatus>(status, true);
        if (!string.IsNullOrWhiteSpace(kind) && !Enum.TryParse(kind, true, out AppointmentKind k0)) return BadRequest(ApiResponse<object>.Fail("kind geçersiz.", 400)); else if (!string.IsNullOrWhiteSpace(kind)) kd = Enum.Parse<AppointmentKind>(kind, true);

        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);
        var rows = await _appointments.ListAsync(fromUtc, toUtc, projectId, customerId, assignedUserId, st, kd, cancellationToken);
        return OkResponse(rows.Select(ToDto).ToList());
    }

    /// <summary>GET /api/v1/appointments/customer/{customerId} — müşterinin planlı + son randevuları.</summary>
    [HttpGet("customer/{customerId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<List<AppointmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForCustomer(Guid customerId, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await _appointments.ListForCustomerAsync(customerId, Math.Clamp(limit, 1, 100), cancellationToken);
        return OkResponse(rows.Select(ToDto).ToList());
    }

    // ── Oluştur / düzenle ────────────────────────────────────────────────────

    /// <summary>POST /api/v1/appointments — elle randevu (müşteri detayı ya da takvim).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentBody body, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(body.Kind, true, out AppointmentKind kind)) return BadRequest(ApiResponse<object>.Fail("kind geçersiz.", 400));
        if (body.ScheduledAt == default) return BadRequest(ApiResponse<object>.Fail("Tarih ve saat gerekli.", 400));
        if (body.Title is { Length: > 200 }) return BadRequest(ApiResponse<object>.Fail("Başlık en fazla 200 karakter.", 400));

        var customer = await _customers.GetByIdAsync(body.CustomerId, cancellationToken);
        if (customer is null) return NotFound(ApiResponse<object>.Fail("Müşteri bulunamadı.", 404));
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId)) return ForbiddenResponse<object>("Bu müşteriye erişim yok.");

        var row = new Appointment
        {
            ProjectId = customer.ProjectId, CustomerId = customer.Id, Kind = kind, Source = AppointmentSource.Manual, Status = AppointmentStatus.Planned,
            ScheduledAt = DateTime.SpecifyKind(body.ScheduledAt.ToUniversalTime(), DateTimeKind.Utc),
            DurationMinutes = Math.Clamp(body.DurationMinutes ?? DefaultDuration(kind), 5, 8 * 60),
            Title = body.Title?.Trim(), Note = body.Note?.Trim(), AssignedUserId = body.AssignedUserId ?? customer.AssignedUserId,
            CreatedByUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId,
        };
        await _appointments.AddAsync(row, cancellationToken);
        var saved = await _appointments.GetAsync(row.Id, cancellationToken) ?? row;
        return StatusCode(201, ApiResponse<AppointmentDto>.Created(ToDto(saved)));
    }

    /// <summary>PUT /api/v1/appointments/{id} — tür / zaman / süre / başlık / not / atanan.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppointmentBody body, CancellationToken cancellationToken = default)
    {
        var row = await _appointments.GetAsync(id, cancellationToken);
        if (row is null) return NotFound(ApiResponse<object>.Fail("Randevu bulunamadı.", 404));
        if (row.Status == AppointmentStatus.Completed) return StatusCode(409, ApiResponse<object>.Fail("Tamamlanmış randevu düzenlenemez.", 409));

        if (body.Kind is { } k) { if (!Enum.TryParse(k, true, out AppointmentKind kind)) return BadRequest(ApiResponse<object>.Fail("kind geçersiz.", 400)); row.Kind = kind; }
        if (body.ScheduledAt is { } at) row.ScheduledAt = DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc);
        if (body.DurationMinutes is { } d) row.DurationMinutes = Math.Clamp(d, 5, 8 * 60);
        if (body.Title is not null) row.Title = body.Title.Trim().Length == 0 ? null : body.Title.Trim();
        if (body.Note is not null) row.Note = body.Note.Trim().Length == 0 ? null : body.Note.Trim();
        if (body.AssignedUserId is not null) row.AssignedUserId = body.AssignedUserId == Guid.Empty ? null : body.AssignedUserId;
        if (body.Status is "Planned" && row.Status is AppointmentStatus.Skipped or AppointmentStatus.NoAnswer) { row.Status = AppointmentStatus.Planned; row.SkipReason = null; }

        await _appointments.UpdateAsync(row, cancellationToken);
        return OkResponse(ToDto(row));
    }

    /// <summary>
    /// POST /api/v1/appointments/{id}/complete — görüşme kaydı girer ve randevuyu kapatır.
    /// reached=false → durum "Ulaşılamadı"; rescheduleAt verilirse aynı türde yeni randevu açılır.
    /// nextContactAt verilirse (söz verilen takip) o güne takip randevusu açılır.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(ApiResponse<CompleteAppointmentResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteAppointmentBody body, CancellationToken cancellationToken = default)
    {
        var row = await _appointments.GetAsync(id, cancellationToken);
        if (row is null) return NotFound(ApiResponse<object>.Fail("Randevu bulunamadı.", 404));
        if (row.Status == AppointmentStatus.Completed) return StatusCode(409, ApiResponse<object>.Fail("Randevu zaten tamamlanmış.", 409));
        if (!Enum.TryParse(body.Type, true, out ContactType type)) return BadRequest(ApiResponse<object>.Fail("type geçersiz (Call, Email, Meeting, Note, WhatsApp, Visit).", 400));
        if (body.SatisfactionScore is { } sc && sc is < 1 or > 5) return BadRequest(ApiResponse<object>.Fail("Memnuniyet puanı 1–5 olmalı.", 400));
        if (body.Subject is { Length: > 500 } || body.Outcome is { Length: > 300 } || body.Content is { Length: > 4000 })
            return BadRequest(ApiResponse<object>.Fail("Metin alanları sınırı aşıyor (konu 500, içerik 4000, sonuç 300).", 400));

        var now = DateTime.UtcNow;
        var userId = _currentUser.UserId == Guid.Empty ? (Guid?)null : _currentUser.UserId;
        var contact = new Domain.Entities.ContactHistory
        {
            CustomerId = row.CustomerId, ProjectId = row.ProjectId, Type = type,
            Subject = string.IsNullOrWhiteSpace(body.Subject) ? DefaultSubject(row.Kind) : body.Subject.Trim(),
            Content = body.Content?.Trim(), Outcome = body.Outcome?.Trim(),
            ContactedAt = body.ContactedAt?.ToUniversalTime() ?? now, CreatedByUserId = userId,
            Purpose = row.Kind, Reached = body.Reached, SatisfactionScore = row.Kind == AppointmentKind.SatisfactionCall || body.SatisfactionScore is not null ? body.SatisfactionScore : null,
            NextContactAt = body.NextContactAt?.ToUniversalTime(), AppointmentId = row.Id,
        };
        await _contacts.AddAsync(contact, cancellationToken);

        row.Status = body.Reached ? AppointmentStatus.Completed : AppointmentStatus.NoAnswer;
        row.CompletedAt = now; row.ContactHistoryId = contact.Id;
        await _appointments.UpdateAsync(row, cancellationToken);

        Appointment? followUp = null;
        if (!body.Reached && body.RescheduleAt is { } re)
        {
            followUp = new Appointment
            {
                ProjectId = row.ProjectId, CustomerId = row.CustomerId, Kind = row.Kind, Source = AppointmentSource.Manual, Status = AppointmentStatus.Planned,
                ScheduledAt = DateTime.SpecifyKind(re.ToUniversalTime(), DateTimeKind.Utc), DurationMinutes = row.DurationMinutes,
                Reason = "Ulaşılamadı — yeniden planlandı", AssignedUserId = row.AssignedUserId, CreatedByUserId = userId,
            };
        }
        else if (body.NextContactAt is { } next)
        {
            var at = DateTime.SpecifyKind(next.ToUniversalTime(), DateTimeKind.Utc);
            if (at.TimeOfDay == TimeSpan.Zero) at = TimeZoneInfo.ConvertTimeToUtc(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(at, Tz)).ToDateTime(new TimeOnly(14, 0), DateTimeKind.Unspecified), Tz);
            followUp = new Appointment
            {
                ProjectId = row.ProjectId, CustomerId = row.CustomerId, Kind = row.Kind == AppointmentKind.NewCall ? AppointmentKind.NewCall : AppointmentKind.FollowUpCall,
                Source = AppointmentSource.Manual, Status = AppointmentStatus.Planned, ScheduledAt = at, DurationMinutes = 15,
                Reason = "Görüşmede söz verilen takip", AssignedUserId = row.AssignedUserId, CreatedByUserId = userId,
            };
        }
        if (followUp is not null) await _appointments.AddAsync(followUp, cancellationToken);

        _logger.LogInformation("Randevu kapatıldı: {Id} ({Kind}) → {Status}, görüşme {ContactId}", row.Id, row.Kind, row.Status, contact.Id);
        var saved = await _appointments.GetAsync(row.Id, cancellationToken) ?? row;
        var savedFollow = followUp is null ? null : await _appointments.GetAsync(followUp.Id, cancellationToken) ?? followUp;
        return OkResponse(new CompleteAppointmentResult(ToDto(saved), contact.Id, savedFollow is null ? null : ToDto(savedFollow)));
    }

    /// <summary>POST /api/v1/appointments/{id}/skip — yapılmayacak (sebep opsiyonel).</summary>
    [HttpPost("{id:guid}/skip")]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(Guid id, [FromBody] SkipAppointmentBody? body, CancellationToken cancellationToken = default)
    {
        var row = await _appointments.GetAsync(id, cancellationToken);
        if (row is null) return NotFound(ApiResponse<object>.Fail("Randevu bulunamadı.", 404));
        if (row.Status == AppointmentStatus.Completed) return StatusCode(409, ApiResponse<object>.Fail("Tamamlanmış randevu atlanamaz.", 409));
        row.Status = AppointmentStatus.Skipped; row.SkipReason = body?.Reason?.Trim() is { Length: > 0 } r ? r[..Math.Min(r.Length, 300)] : null; row.CompletedAt = DateTime.UtcNow;
        await _appointments.UpdateAsync(row, cancellationToken);
        return OkResponse(ToDto(row));
    }

    /// <summary>DELETE /api/v1/appointments/{id} — soft delete.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _appointments.GetAsync(id, cancellationToken);
        if (row is null) return NotFound(ApiResponse<object>.Fail("Randevu bulunamadı.", 404));
        row.IsDeleted = true;
        await _appointments.UpdateAsync(row, cancellationToken);
        return OkResponse(new { deleted = true });
    }

    // ── CSO planı ────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/appointments/plan/status?projectId= — kotalar, son koşu.</summary>
    [HttpGet("plan/status")]
    [ProducesResponseType(typeof(ApiResponse<CallPlanStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanStatus([FromQuery] Guid? projectId, CancellationToken cancellationToken = default)
    {
        var pid = projectId ?? _currentUser.ProjectIds.FirstOrDefault();
        CallPlanRun? run = pid == Guid.Empty ? null : await _appointments.GetLatestRunAsync(pid, cancellationToken);
        var q = _plan.Quotas;
        return OkResponse(new CallPlanStatusDto(
            q.NewCalls, q.FollowUps, q.Satisfaction, _configuration.GetValue("CallPlan:HorizonBusinessDays", 5), _configuration["CallPlan:TimeZone"] ?? "Europe/Istanbul",
            _configuration.GetValue("CallPlan:RunHourLocal", 5), _configuration.GetValue("CallPlan:Enabled", true),
            run?.PlanDate, run?.RanAt, run?.CreatedCount, run?.Summary, CanRunPlan(pid)));
    }

    /// <summary>POST /api/v1/appointments/plan/run?projectId=&amp;force= — planlayıcıyı şimdi koştur (boş slotları doldurur).</summary>
    [HttpPost("plan/run")]
    [ProducesResponseType(typeof(ApiResponse<CallPlanRunResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanRun([FromQuery] Guid? projectId, [FromQuery] bool force = true, CancellationToken cancellationToken = default)
    {
        var pid = projectId ?? _currentUser.ProjectIds.FirstOrDefault();
        if (pid == Guid.Empty) return BadRequest(ApiResponse<object>.Fail("Proje seçili değil.", 400));
        if (!CanRunPlan(pid)) return ForbiddenResponse<object>("Planı yalnız SuperAdmin ya da proje yöneticisi / satış yöneticisi çalıştırabilir.");
        var result = await _plan.RunForProjectAsync(pid, force, cancellationToken);
        return OkResponse(result);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private bool CanRunPlan(Guid projectId)
    {
        if (_currentUser.IsSuperAdmin) return true;
        if (projectId == Guid.Empty || !_currentUser.ProjectIds.Contains(projectId)) return false;
        var role = _currentUser.GetRoleForProject(projectId);
        return role is "ProjectAdmin" or "SalesManager";
    }

    private static bool TryDay(string? s, DateOnly fallback, out DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(s)) { day = fallback; return true; }
        return DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    private static int DefaultDuration(AppointmentKind kind) => kind switch
    {
        AppointmentKind.Meeting => 60, AppointmentKind.Training => 90, AppointmentKind.Demo => 45, AppointmentKind.Other => 30, _ => 15,
    };

    private static string DefaultSubject(AppointmentKind kind) => kind switch
    {
        AppointmentKind.NewCall => "Yeni arama", AppointmentKind.FollowUpCall => "Takip araması", AppointmentKind.SatisfactionCall => "Memnuniyet araması",
        AppointmentKind.Meeting => "Toplantı", AppointmentKind.Training => "Eğitim", AppointmentKind.Demo => "Demo", _ => "Görüşme",
    };

    internal static AppointmentDto ToDto(Appointment a) => new(
        a.Id, a.ProjectId, a.CustomerId, a.Customer?.CompanyName ?? string.Empty, a.Customer?.Phone, a.Customer?.ContactName,
        a.Customer?.Status.ToString() ?? string.Empty, a.Customer?.Label?.ToString(), a.Customer?.LegacyId,
        a.Kind.ToString(), a.Source.ToString(), a.Status.ToString(), a.ScheduledAt, a.DurationMinutes, a.Title, a.Note, a.Reason, a.Score,
        a.AssignedUserId, a.AssignedUser is null ? null : $"{a.AssignedUser.FirstName} {a.AssignedUser.LastName}".Trim(),
        a.ContactHistoryId, a.CompletedAt, a.SkipReason, a.CreatedAt);
}

public record AppointmentDto(
    Guid Id, Guid ProjectId, Guid CustomerId, string CustomerName, string? CustomerPhone, string? CustomerContactName, string CustomerStatus, string? CustomerLabel, string? CustomerLegacyId,
    string Kind, string Source, string Status, DateTime ScheduledAt, int DurationMinutes, string? Title, string? Note, string? Reason, int Score,
    Guid? AssignedUserId, string? AssignedUserName, Guid? ContactHistoryId, DateTime? CompletedAt, string? SkipReason, DateTime CreatedAt);

public record CreateAppointmentBody(Guid CustomerId, string Kind, DateTime ScheduledAt, int? DurationMinutes, string? Title, string? Note, Guid? AssignedUserId);

public record UpdateAppointmentBody(string? Kind, DateTime? ScheduledAt, int? DurationMinutes, string? Title, string? Note, Guid? AssignedUserId, string? Status);

/// <summary>Görüşme kaydı + kapanış. reached=false iken rescheduleAt yeni randevu açar; nextContactAt söz verilen takibi açar.</summary>
public record CompleteAppointmentBody(string Type, string? Subject, string? Content, string? Outcome, bool Reached, int? SatisfactionScore, DateTime? NextContactAt, DateTime? RescheduleAt, DateTime? ContactedAt);

public record CompleteAppointmentResult(AppointmentDto Appointment, Guid ContactHistoryId, AppointmentDto? FollowUp);

public record SkipAppointmentBody(string? Reason);

public record CallPlanStatusDto(
    int NewCallsPerDay, int FollowUpsPerDay, int SatisfactionPerDay, int HorizonBusinessDays, string TimeZone, int RunHourLocal, bool Enabled,
    DateOnly? LastPlanDate, DateTime? LastRanAt, int? LastCreatedCount, string? LastSummary, bool CanRun);
