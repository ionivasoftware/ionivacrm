using IonCrm.API.Common;
using IonCrm.Application.Features.Parasut.Commands.ConnectParasut;
using IonCrm.Application.Features.Parasut.Commands.CreateSalesInvoice;
using IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;
using IonCrm.Application.Features.Parasut.Commands.LinkParasutContact;
using IonCrm.Application.Features.Parasut.Commands.SyncContactToParasut;
using IonCrm.Application.Features.Parasut.Queries.GetParasutContactInvoices;
using IonCrm.Application.Features.Parasut.Queries.GetParasutContacts;
using IonCrm.Application.Features.Parasut.Queries.GetParasutInvoices;
using IonCrm.Application.Features.Parasut.Queries.GetParasutStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for managing the Paraşüt accounting integration.
///
/// Typical workflow:
///   1. POST /parasut/connect        — authenticate with Paraşüt OAuth and save tokens
///   2. GET  /parasut/status         — verify connection is active
///   3. GET  /parasut/contacts       — browse Paraşüt cariler
///   4. POST /parasut/contacts/sync  — push a CRM customer to Paraşüt
///   5. GET  /parasut/invoices       — browse Paraşüt faturaları
///   6. POST /parasut/invoices       — create a new satış faturası
///   7. DELETE /parasut/disconnect   — remove the connection
/// </summary>
[ApiController]
[Route("api/v1/parasut")]
[Authorize]
public class ParasutController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>Initialises a new instance of <see cref="ParasutController"/>.</summary>
    public ParasutController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/parasut/status?projectId={guid?}
    /// Returns connection status. Omit <c>projectId</c> to query the global connection.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid? projectId = null)
    {
        var result = await _mediator.Send(new GetParasutStatusQuery(projectId));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<ParasutStatusDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/parasut/connect
    /// Authenticate with Paraşüt OAuth and store access + refresh tokens.
    /// Requires ProjectAdmin or SuperAdmin.
    /// </summary>
    [HttpPost("connect")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Connect([FromBody] ConnectParasutCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<ConnectParasutDto>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/parasut/disconnect?projectId={guid}
    /// Removes the Paraşüt connection for the given project.
    /// Omit <c>projectId</c> to disconnect the global connection.
    /// Requires SuperAdmin.
    /// </summary>
    [HttpDelete("disconnect")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Disconnect([FromQuery] Guid? projectId = null)
    {
        var result = await _mediator.Send(new DisconnectParasutCommand(projectId));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<object>.Ok(new { message = "Paraşüt bağlantısı kaldırıldı." }));
    }

    // ── Contacts ──────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/parasut/contacts
    /// Returns a paginated list of Paraşüt contacts (cariler) for the project.
    /// </summary>
    [HttpGet("contacts")]
    public async Task<IActionResult> GetContacts(
        [FromQuery] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetParasutContactsQuery(projectId, page, pageSize, search));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<GetParasutContactsDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/parasut/contacts/sync
    /// Pushes a CRM customer to Paraşüt as a contact (cari). Creates or updates.
    /// </summary>
    [HttpPost("contacts/sync")]
    public async Task<IActionResult> SyncContact([FromBody] SyncContactToParasutCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<SyncContactToParasutDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/parasut/contacts/link
    /// Links an existing Paraşüt contact (by its ID) to a CRM customer.
    /// </summary>
    [HttpPost("contacts/link")]
    public async Task<IActionResult> LinkContact([FromBody] LinkParasutContactCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<LinkParasutContactDto>.Ok(result.Value!));
    }

    // ── Sales Invoices ────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/parasut/invoices
    /// Returns a paginated list of Paraşüt sales invoices (satış faturaları).
    /// </summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _mediator.Send(new GetParasutInvoicesQuery(projectId, page, pageSize));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<GetParasutInvoicesDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/parasut/invoices
    /// Creates a new sales invoice (satış faturası) in Paraşüt.
    /// </summary>
    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateParasutSalesInvoiceCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<CreateParasutSalesInvoiceDto>.Ok(result.Value!));
    }

    // ── Contact Invoices (Cari Hareketleri) ──────────────────────────────────

    /// <summary>
    /// GET /api/v1/parasut/contacts/{parasutContactId}/invoices
    /// Returns paginated sales invoices for a specific Paraşüt contact (cari hareketleri).
    /// </summary>
    [HttpGet("contacts/{parasutContactId}/invoices")]
    public async Task<IActionResult> GetContactInvoices(
        string parasutContactId,
        [FromQuery] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _mediator.Send(
            new GetParasutContactInvoicesQuery(projectId, parasutContactId, page, pageSize));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<GetParasutInvoicesDto>.Ok(result.Value!));
    }
}
