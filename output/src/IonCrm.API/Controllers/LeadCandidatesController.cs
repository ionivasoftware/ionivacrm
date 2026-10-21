using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>Lead-agent approval queue (SuperAdmin): review, approve → Customer(Lead), reject, mark duplicate; agent run history.</summary>
[Route("api/v1/lead-candidates")]
[Authorize(Policy = "SuperAdmin")]
public sealed class LeadCandidatesController : ApiControllerBase
{
    private readonly ILeadCandidateRepository _repo;
    private readonly IContactHistoryRepository _contacts;
    private readonly ICurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LeadCandidatesController> _logger;

    public LeadCandidatesController(ILeadCandidateRepository repo, IContactHistoryRepository contacts, ICurrentUserService currentUser, IConfiguration configuration, ILogger<LeadCandidatesController> logger)
    {
        _repo = repo; _contacts = contacts; _currentUser = currentUser; _configuration = configuration; _logger = logger;
    }

    /// <summary>GET /api/v1/lead-candidates?status=&amp;search=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        LeadCandidateStatus? st = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, true, out LeadCandidateStatus parsed)) return BadRequest(ApiResponse<object>.Fail("status Pending | Approved | Rejected | Duplicate olmalı.", 400));
            st = parsed;
        }
        var (items, total) = await _repo.ListAsync(st, search, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken);
        return OkResponse(new { items = items.Select(ToDto).ToList(), totalCount = total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 100) });
    }

    /// <summary>GET /api/v1/lead-candidates/meta — counts, projects, default project, agent key configured.</summary>
    [HttpGet("meta")]
    public async Task<IActionResult> Meta(CancellationToken cancellationToken = default)
    {
        var counts = await _repo.CountsAsync(cancellationToken);
        var projects = await _repo.ProjectsAsync(cancellationToken);
        var defaultProject = await _repo.ResolveDefaultProjectIdAsync(Guid.TryParse(_configuration["LeadAgent:ProjectId"], out var cfg) ? cfg : null, cancellationToken);
        return OkResponse(new
        {
            counts = new
            {
                pending = counts.GetValueOrDefault(LeadCandidateStatus.Pending), approved = counts.GetValueOrDefault(LeadCandidateStatus.Approved),
                rejected = counts.GetValueOrDefault(LeadCandidateStatus.Rejected), duplicate = counts.GetValueOrDefault(LeadCandidateStatus.Duplicate),
            },
            projects = projects.Select(p => new { id = p.Id, name = p.Name }),
            defaultProjectId = defaultProject,
            agentKeyConfigured = !string.IsNullOrWhiteSpace(_configuration["LeadAgent:ApiKey"]),
        });
    }

    /// <summary>GET /api/v1/lead-candidates/pending-count — menü rozeti.</summary>
    [HttpGet("pending-count")]
    public async Task<IActionResult> PendingCount(CancellationToken cancellationToken = default)
    {
        var counts = await _repo.CountsAsync(cancellationToken);
        return OkResponse(new { count = counts.GetValueOrDefault(LeadCandidateStatus.Pending) });
    }

    /// <summary>GET /api/v1/lead-candidates/runs?limit=20 — ajan koşuları.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var runs = (await _repo.ListRunsAsync(Math.Clamp(limit, 1, 100) * 2, cancellationToken))
            .Where(r => !(r.Cities.Length >= 3 && r.Cities[2] == ':' && char.IsUpper(r.Cities[0]) && char.IsUpper(r.Cities[1]))) // global ajan koşuları ayrı ekranda
            .Take(Math.Clamp(limit, 1, 100));
        return OkResponse(runs.Select(r => new { r.RunId, r.StartedAt, r.FinishedAt, r.Cities, r.Found, r.Created, r.Duplicates, r.Existing, r.Invalid, r.Notes }).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _repo.GetAsync(id, cancellationToken);
        return c is null ? NotFound(ApiResponse<object>.Fail("Aday bulunamadı.", 404)) : OkResponse(ToDto(c));
    }

    /// <summary>POST /api/v1/lead-candidates/{id}/approve — Lead müşteri oluşturur (alanlar düzenlenmiş hâliyle).</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveLeadBody? body, CancellationToken cancellationToken = default)
    {
        var c = await _repo.GetAsync(id, cancellationToken);
        if (c is null) return NotFound(ApiResponse<object>.Fail("Aday bulunamadı.", 404));
        if (c.Status == LeadCandidateStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Aday zaten onaylanmış.", 409));

        var projectId = body?.ProjectId ?? c.ProjectId;
        CustomerLabel? label = null;
        if (!string.IsNullOrWhiteSpace(body?.Label))
        {
            if (!Enum.TryParse(body.Label, true, out CustomerLabel l)) return BadRequest(ApiResponse<object>.Fail("label geçersiz.", 400));
            label = l;
        }
        var name = (body?.CompanyName ?? c.CompanyName).Trim();
        if (name.Length is 0 or > 300) return BadRequest(ApiResponse<object>.Fail("Firma adı 1–300 karakter olmalı.", 400));

        var result = await Mediator.Send(new CreateCustomerCommand
        {
            ProjectId = projectId, CompanyName = name, ContactName = body?.ContactName?.Trim(), Email = (body?.Email ?? c.Email)?.Trim(),
            Phone = (body?.Phone ?? c.Phone)?.Trim(), Address = (body?.Address ?? ComposeAddress(c))?.Trim(), TaxNumber = (body?.TaxNumber ?? c.TaxNumber)?.Trim(),
            Status = CustomerStatus.Lead, Label = label ?? CustomerLabel.Potansiyel, Segment = body?.Segment?.Trim(), AssignedUserId = body?.AssignedUserId,
        }, cancellationToken);
        if (!result.IsSuccess || result.Value is null) return BadRequest(ApiResponse<object>.Fail(result.FirstError ?? "Müşteri oluşturulamadı.", 400));

        // Provenance on the timeline: where the lead came from.
        try
        {
            await _contacts.AddAsync(new Domain.Entities.ContactHistory
            {
                CustomerId = result.Value.Id, ProjectId = projectId, Type = ContactType.Note, Subject = "Lead ajanı buldu",
                Content = $"Kaynak: {c.Source}{(c.SourceUrl is null ? "" : $" — {c.SourceUrl}")}\n{(c.Snippet ?? string.Empty)}".Trim(),
                ContactedAt = DateTime.UtcNow, CreatedByUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId,
            }, cancellationToken);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Lead kaynağı notu yazılamadı"); }

        c.Status = LeadCandidateStatus.Approved; c.CreatedCustomerId = result.Value.Id; c.ReviewedAt = DateTime.UtcNow; c.ReviewedBy = _currentUser.Email; c.RejectReason = null;
        await _repo.UpdateAsync(c, cancellationToken);
        _logger.LogInformation("Lead adayı onaylandı: {Name} → müşteri {CustomerId}", name, result.Value.Id);
        return OkResponse(new { candidate = ToDto(c), customerId = result.Value.Id });
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeadBody? body, CancellationToken cancellationToken = default)
    {
        var c = await _repo.GetAsync(id, cancellationToken);
        if (c is null) return NotFound(ApiResponse<object>.Fail("Aday bulunamadı.", 404));
        if (c.Status == LeadCandidateStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Onaylanmış aday reddedilemez.", 409));
        c.Status = LeadCandidateStatus.Rejected; c.RejectReason = Trunc(body?.Reason, 500); c.ReviewedAt = DateTime.UtcNow; c.ReviewedBy = _currentUser.Email;
        await _repo.UpdateAsync(c, cancellationToken);
        return OkResponse(ToDto(c));
    }

    /// <summary>POST /api/v1/lead-candidates/reject-many — {ids[], reason?}.</summary>
    [HttpPost("reject-many")]
    public async Task<IActionResult> RejectMany([FromBody] RejectManyBody body, CancellationToken cancellationToken = default)
    {
        var rows = await _repo.GetManyAsync(body.Ids ?? new(), cancellationToken);
        var n = 0;
        foreach (var c in rows.Where(r => r.Status != LeadCandidateStatus.Approved))
        {
            c.Status = LeadCandidateStatus.Rejected; c.RejectReason = Trunc(body.Reason, 500); c.ReviewedAt = DateTime.UtcNow; c.ReviewedBy = _currentUser.Email;
            await _repo.UpdateAsync(c, cancellationToken); n++;
        }
        return OkResponse(new { rejected = n });
    }

    /// <summary>POST /api/v1/lead-candidates/{id}/duplicate — {customerId}: elle mükerrer işaretle.</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> MarkDuplicate(Guid id, [FromBody] MarkDuplicateBody body, CancellationToken cancellationToken = default)
    {
        var c = await _repo.GetAsync(id, cancellationToken);
        if (c is null) return NotFound(ApiResponse<object>.Fail("Aday bulunamadı.", 404));
        if (c.Status == LeadCandidateStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Onaylanmış aday işaretlenemez.", 409));
        c.Status = LeadCandidateStatus.Duplicate; c.DuplicateOfCustomerId = body.CustomerId; c.MatchNotes = "Elle mükerrer işaretlendi"; c.ReviewedAt = DateTime.UtcNow; c.ReviewedBy = _currentUser.Email;
        await _repo.UpdateAsync(c, cancellationToken);
        return OkResponse(ToDto(c));
    }

    /// <summary>POST /api/v1/lead-candidates/{id}/reopen — reddedilmiş/mükerrer adayı bekleyene al.</summary>
    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _repo.GetAsync(id, cancellationToken);
        if (c is null) return NotFound(ApiResponse<object>.Fail("Aday bulunamadı.", 404));
        if (c.Status == LeadCandidateStatus.Approved) return StatusCode(409, ApiResponse<object>.Fail("Onaylanmış aday geri alınamaz.", 409));
        c.Status = LeadCandidateStatus.Pending; c.RejectReason = null; c.ReviewedAt = null; c.ReviewedBy = null;
        await _repo.UpdateAsync(c, cancellationToken);
        return OkResponse(ToDto(c));
    }

    private static string? ComposeAddress(LeadCandidate c)
    {
        var parts = new[] { c.Address, c.District, c.City }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct().ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? Trunc(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    internal static LeadCandidateDto ToDto(LeadCandidate c) => new(
        c.Id, c.ProjectId, c.CompanyName, c.LegalTitle, c.Phone, c.Email, c.Website, c.Address, c.City, c.District, c.TaxNumber, c.MersisNo,
        c.Source, c.SourceUrl, c.Snippet, c.Confidence, c.Status.ToString(), c.DuplicateOfCustomerId, c.PossibleMatchCustomerId, c.MatchNotes,
        c.AgentRunId, c.SearchCity, c.ReviewedBy, c.ReviewedAt, c.RejectReason, c.CreatedCustomerId, c.CreatedAt);
}

public record LeadCandidateDto(
    Guid Id, Guid ProjectId, string CompanyName, string? LegalTitle, string? Phone, string? Email, string? Website, string? Address, string? City, string? District,
    string? TaxNumber, string? MersisNo, string Source, string? SourceUrl, string? Snippet, int Confidence, string Status, Guid? DuplicateOfCustomerId,
    Guid? PossibleMatchCustomerId, string? MatchNotes, string? AgentRunId, string? SearchCity, string? ReviewedBy, DateTime? ReviewedAt, string? RejectReason,
    Guid? CreatedCustomerId, DateTime CreatedAt);

public record ApproveLeadBody(Guid? ProjectId, string? CompanyName, string? ContactName, string? Phone, string? Email, string? Address, string? TaxNumber, string? Label, string? Segment, Guid? AssignedUserId);

public record RejectLeadBody(string? Reason);

public record RejectManyBody(List<Guid>? Ids, string? Reason);

public record MarkDuplicateBody(Guid CustomerId);
