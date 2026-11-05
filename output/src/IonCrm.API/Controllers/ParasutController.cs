using IonCrm.API.Common;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Features.Parasut;
using IonCrm.Application.Features.Parasut.Commands.CompleteParasutOAuth;
using IonCrm.Application.Features.Parasut.Commands.ConnectParasut;
using IonCrm.Application.Features.Parasut.Commands.CreateSalesInvoice;
using IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;
using IonCrm.Application.Features.Parasut.Commands.StartParasutOAuth;
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
///   1. POST /parasut/connect/oauth  — get Paraşüt's login URL (authorization code + PKCE); the browser comes back to
///      GET /parasut/callback, which stores the tokens and redirects to the CRM settings page.
///      (POST /parasut/connect — legacy password grant — stays until Paraşüt retires it, ~05.12.2026.)
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
    private readonly IConfiguration _configuration;

    /// <summary>Initialises a new instance of <see cref="ParasutController"/>.</summary>
    public ParasutController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
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
    /// POST /api/v1/parasut/connect/oauth — NEW flow. Starts the authorization-code + PKCE flow for the global connection and
    /// returns the Paraşüt login URL; the browser is sent there and comes back to <see cref="Callback"/>.
    /// Requires SuperAdmin. The Paraşüt user must have the "Uygulama Entegrasyonları" permission (off by default).
    /// </summary>
    [HttpPost("connect/oauth")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> ConnectOAuth()
    {
        var result = await _mediator.Send(new StartParasutOAuthCommand());
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<StartParasutOAuthDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/parasut/callback?code=&amp;state=  — Paraşüt's redirect back. ANONYMOUS: the browser carries no JWT here; the only
    /// trust anchor is the single-use <c>state</c>. Stores the tokens and redirects to the settings page with
    /// <c>?parasut=connected|error&amp;reason=…</c>. The path is registered at Paraşüt and cannot change in production.
    /// Lives in this controller on purpose: the CRM has no authorization filter that would swallow [AllowAnonymous].
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var result = await _mediator.Send(new CompleteParasutOAuthCommand(code, state, error));

        var frontend = AppUrls.FrontendBaseUrl(key => _configuration[key]);
        if (frontend is not null)
        {
            var query = $"parasut={Uri.EscapeDataString(result.Outcome)}" + (result.Reason is null ? string.Empty : $"&reason={Uri.EscapeDataString(result.Reason)}");
            return Redirect($"{frontend}/settings?{query}");
        }

        // Frontend address not configured (App__FrontendUrl): plain result, the tab can be closed.
        return Content($"Paraşüt: {result.Outcome}{(result.Reason is null ? string.Empty : $" ({result.Reason})")}", "text/plain; charset=utf-8");
    }

    /// <summary>
    /// POST /api/v1/parasut/connect — LEGACY password grant (stored Paraşüt e-mail + password). Paraşüt retires this
    /// around 05.12.2026; kept untouched until then. Requires SuperAdmin.
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
    /// Removes the Paraşüt connection for the given project (new-flow rows are revoked at Paraşüt first).
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
