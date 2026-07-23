using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only proxy over the Liftdesk (EMS) CRM ticket API (docs/crm-ticket-api.md).
///
/// GET   /api/v1/tickets              — filtered ticket list (status/type/platform/projectId, paged)
/// GET   /api/v1/tickets/{id}         — single ticket detail
/// POST  /api/v1/tickets              — support team opens a ticket
/// PATCH /api/v1/tickets/{id}/status  — approve / reject (or re-approve a Failed ticket)
///
/// Credentials never reach the browser: auth is the static M2M Bearer key inside
/// <see cref="ILiftdeskTicketClient"/>. AI triage and the fix pipeline run on the EMS side; the CRM
/// only lists, opens and decides. <c>decidedBy</c> is derived from the authenticated SuperAdmin
/// (not client-supplied) to prevent spoofing — matching the error-triage screen.
/// </summary>
[Route("api/v1/tickets")]
[Authorize(Policy = "SuperAdmin")]
public sealed class TicketsController : ApiControllerBase
{
    private readonly ILiftdeskTicketClient _ticketClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TicketsController> _logger;

    /// <summary>Initialises a new instance of <see cref="TicketsController"/>.</summary>
    public TicketsController(
        ILiftdeskTicketClient ticketClient,
        ICurrentUserService currentUser,
        ILogger<TicketsController> logger)
    {
        _ticketClient = ticketClient;
        _currentUser  = currentUser;
        _logger       = logger;
    }

    /// <summary>
    /// Lists tickets from Liftdesk, filtered and paged. All filters optional.
    /// GET /api/v1/tickets?status=New&amp;type=Suggestion&amp;platform=Web&amp;projectId=&amp;page=1&amp;pageSize=20
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskPage<LiftdeskTicket>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTickets(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? platform,
        [FromQuery] Guid? projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_ticketClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        page     = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

        try
        {
            var envelope = await _ticketClient.GetTicketsAsync(
                Trim(status), Trim(type), Trim(platform), projectId, page, pageSize, cancellationToken);

            if (!envelope.Success)
                return BadRequest(ApiResponse<object>.Fail(envelope.Message ?? "Talepler alınamadı.", 400));

            var pageResult = envelope.Data ?? new LiftdeskPage<LiftdeskTicket>(
                new List<LiftdeskTicket>(), 0, page, pageSize, 0, false, false);
            return OkResponse(pageResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liftdesk ticket listesi alınamadı.");
            return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
        }
    }

    /// <summary>Returns a single ticket's full detail. GET /api/v1/tickets/{id}.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskTicket>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_ticketClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        try
        {
            var envelope = await _ticketClient.GetTicketAsync(id, cancellationToken);
            if (!envelope.Success || envelope.Data is null)
            {
                var msg = envelope.Message ?? "Talep bulunamadı.";
                if (envelope.StatusCode == 404)
                    return NotFound(ApiResponse<object>.Fail(msg, 404));
                return BadRequest(ApiResponse<object>.Fail(msg, 400));
            }

            return OkResponse(envelope.Data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liftdesk ticket {Id} alınamadı.", id);
            return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
        }
    }

    /// <summary>
    /// Opens a support ticket on the Liftdesk side (Source=Crm, Status=New). ProjectId null → global.
    /// <c>createdByName</c> defaults to the SuperAdmin's identity when omitted.
    /// POST /api/v1/tickets
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskTicket>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTicket(
        [FromBody] CreateTicketRequest body,
        CancellationToken cancellationToken = default)
    {
        if (!_ticketClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        var type     = body?.Type?.Trim();
        var platform = body?.Platform?.Trim();
        var subject  = body?.Subject?.Trim();
        var desc     = body?.Description?.Trim();

        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(platform))
            return BadRequest(ApiResponse<object>.Fail("Tür ve platform zorunludur.", 400));
        if (string.IsNullOrWhiteSpace(subject))
            return BadRequest(ApiResponse<object>.Fail("Konu zorunludur.", 400));
        if (string.IsNullOrWhiteSpace(desc))
            return BadRequest(ApiResponse<object>.Fail("Açıklama zorunludur.", 400));

        // Attribute the ticket to the acting SuperAdmin when the support agent didn't name themselves.
        var createdByName = string.IsNullOrWhiteSpace(body!.CreatedByName)
            ? (string.IsNullOrWhiteSpace(_currentUser.Email) ? "CRM" : $"Destek: {_currentUser.Email}")
            : body.CreatedByName.Trim();

        try
        {
            var envelope = await _ticketClient.CreateTicketAsync(
                body.ProjectId, type, platform, subject, desc, createdByName, cancellationToken);

            if (!envelope.Success || envelope.Data is null)
                return BadRequest(ApiResponse<object>.Fail(envelope.Message ?? "Talep oluşturulamadı.", 400));

            return StatusCode(201, ApiResponse<LiftdeskTicket>.Created(envelope.Data, "Talep oluşturuldu."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liftdesk ticket oluşturulamadı.");
            return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
        }
    }

    /// <summary>
    /// Approves or rejects a ticket (or re-approves a Failed one to retry the fix agent).
    /// PATCH /api/v1/tickets/{id}/status — body { status: "Approved" | "Rejected", decisionNote }.
    /// <c>decidedBy</c> is derived from the authenticated SuperAdmin. Liftdesk enforces the state
    /// machine and returns 409 for invalid transitions, surfaced here.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskTicket>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateTicketStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        if (!_ticketClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        var status = body?.Status?.Trim();
        // The CRM only performs the two human decisions; agent transitions (InProgress/Done/Failed)
        // are written directly against Liftdesk by the fix agents, not through this proxy.
        if (status is not ("Approved" or "Rejected"))
            return BadRequest(ApiResponse<object>.Fail("Geçersiz durum. 'Approved' veya 'Rejected' olmalı.", 400));

        var decidedBy    = !string.IsNullOrWhiteSpace(_currentUser.Email) ? _currentUser.Email : "crm";
        var decisionNote = string.IsNullOrWhiteSpace(body!.DecisionNote) ? null : body.DecisionNote.Trim();
        var successMsg   = status == "Approved" ? "Talep onaylandı." : "Talep reddedildi.";

        try
        {
            var envelope = await _ticketClient.UpdateTicketStatusAsync(
                id, status, decidedBy, decisionNote, cancellationToken);

            if (!envelope.Success || envelope.Data is null)
            {
                var msg = envelope.Message ?? "İşlem Liftdesk tarafından reddedildi.";
                if (envelope.StatusCode == 409)
                    return Conflict(ApiResponse<object>.Fail(msg, 409));
                return BadRequest(ApiResponse<object>.Fail(msg, 400));
            }

            return OkResponse(envelope.Data, successMsg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liftdesk ticket {Id} durumu güncellenemedi.", id);
            return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Request body for POST /api/v1/tickets. Type/Platform are enum names (Feedback|Suggestion, Web|MobileStaff|...).</summary>
public record CreateTicketRequest(
    Guid? ProjectId,
    string? Type,
    string? Platform,
    string? Subject,
    string? Description,
    string? CreatedByName);

/// <summary>Request body for PATCH /api/v1/tickets/{id}/status. DecidedBy is ignored (derived server-side).</summary>
public record UpdateTicketStatusRequest(string Status, string? DecisionNote = null);
