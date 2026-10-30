using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.API.Controllers;

/// <summary>
/// Demo yaşam döngüsü e-postaları (TR dışı Liftdesk deneme hesapları): hoş geldin → nasıl gidiyor → deneme
/// bitti hatırlatması. Zamanlayıcı saatlik koşar; bu uçlar durum, adaylar, gönderim geçmişi, ön izleme ve
/// elle gönderim içindir. SuperAdmin.
/// </summary>
[Route("api/v1/lifecycle-mails")]
[Authorize(Policy = "SuperAdmin")]
public sealed class LifecycleMailsController : ApiControllerBase
{
    private readonly IDemoLifecycleService _service;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public LifecycleMailsController(IDemoLifecycleService service, ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _service = service; _db = db; _currentUser = currentUser;
    }

    /// <summary>GET /api/v1/lifecycle-mails/status — açık mı, SMTP var mı, diller, seçenekler, adım sayıları, son döngü.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default) => OkResponse(await _service.GetStatusAsync(cancellationToken));

    /// <summary>GET /api/v1/lifecycle-mails/candidates — kuralların baktığı son deneme hesapları: sıradaki adım ya da atlanma sebebi.</summary>
    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(CancellationToken cancellationToken = default) => OkResponse(await _service.GetCandidatesAsync(cancellationToken));

    /// <summary>GET /api/v1/lifecycle-mails?step=&amp;page=&amp;pageSize= — gönderim geçmişi (müşteri adıyla).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? step, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        LifecycleStep? st = null;
        if (!string.IsNullOrWhiteSpace(step)) { if (!Enum.TryParse(step, true, out LifecycleStep p)) return BadRequest(ApiResponse<object>.Fail("step Welcome | CheckIn | TrialEnded olmalı.", 400)); st = p; }
        var size = Math.Clamp(pageSize, 1, 100); var pg = Math.Max(1, page);
        var q = _db.CustomerLifecycleMails.IgnoreQueryFilters().Where(m => !m.IsDeleted);
        if (st is { } s) q = q.Where(m => m.Step == s);
        var total = await q.CountAsync(cancellationToken);
        var rows = await q.OrderByDescending(m => m.SentAt).Skip((pg - 1) * size).Take(size).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.CustomerId).Distinct().ToList();
        var names = await _db.Customers.IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CompanyName, c.Country, c.Status }).ToListAsync(cancellationToken);
        var items = rows.Select(r =>
        {
            var c = names.FirstOrDefault(n => n.Id == r.CustomerId);
            return new { r.Id, r.CustomerId, companyName = c?.CompanyName ?? "—", country = c?.Country, customerStatus = c?.Status.ToString(), step = r.Step.ToString(), r.Language, r.To, r.Subject, r.SentAt, status = r.Status.ToString(), r.Error, r.TriggeredBy };
        }).ToList();
        return OkResponse(new { items, totalCount = total, page = pg, pageSize = size });
    }

    /// <summary>GET /api/v1/lifecycle-mails/preview?customerId=&amp;step= — şablonun bu müşteri için render'ı (HTML).</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] Guid customerId, [FromQuery] string step, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(step, true, out LifecycleStep st)) return BadRequest(ApiResponse<object>.Fail("step Welcome | CheckIn | TrialEnded olmalı.", 400));
        var r = await _service.PreviewAsync(customerId, st, cancellationToken);
        return r is null ? NotFound(ApiResponse<object>.Fail("Müşteri ya da şablon bulunamadı.", 404)) : OkResponse(new { r.Subject, r.Html });
    }

    /// <summary>POST /api/v1/lifecycle-mails/customers/{customerId}/send — {step, force?}: şimdi gönder (aynı adım ikinci kez gitmez).</summary>
    [HttpPost("customers/{customerId:guid}/send")]
    public async Task<IActionResult> Send(Guid customerId, [FromBody] SendLifecycleBody body, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(body.Step, true, out LifecycleStep st)) return BadRequest(ApiResponse<object>.Fail("step Welcome | CheckIn | TrialEnded olmalı.", 400));
        var err = await _service.SendAsync(customerId, st, body.Force ?? false, _currentUser.Email ?? "manual", cancellationToken);
        return err is null ? OkResponse(new { sent = true }) : BadRequest(ApiResponse<object>.Fail(err, 400));
    }

    /// <summary>POST /api/v1/lifecycle-mails/run — zamanlayıcı döngüsünü şimdi çalıştır.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken cancellationToken = default) => OkResponse(await _service.RunDueAsync(cancellationToken));
}

public record SendLifecycleBody(string Step, bool? Force);
