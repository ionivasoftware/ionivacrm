using IonCrm.API.Common;
using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Features.Invoices.Commands.CreateInvoice;
using IonCrm.Application.Features.Invoices.Commands.ImportParasutInvoices;
using IonCrm.Application.Features.Invoices.Commands.TransferInvoiceToParasut;
using IonCrm.Application.Features.Invoices.Queries.GetInvoiceById;
using IonCrm.Application.Features.Invoices.Queries.GetInvoices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// CRM Invoice management endpoints.
///
/// Two-step invoice flow:
///   1. POST /invoices       — create invoice in CRM DB (Draft)
///   2. POST /invoices/{id}/transfer-to-parasut — transfer to Paraşüt (sets ParasutId)
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/invoices?projectId={projectId}
    /// Returns all CRM invoices for the given project, newest first.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] Guid projectId)
    {
        var result = await _mediator.Send(new GetInvoicesQuery(projectId));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<List<InvoiceDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/invoices/{id}
    /// Returns a single invoice by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetInvoiceById(Guid id)
    {
        var result = await _mediator.Send(new GetInvoiceByIdQuery(id));
        if (result.IsFailure)
            return NotFound(ApiResponse<object>.Fail(result.Errors, 404));
        return Ok(ApiResponse<InvoiceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/invoices
    /// Creates a new invoice in the CRM database with Draft status.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<InvoiceDto>.Created(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/invoices/{id}/transfer-to-parasut
    /// Transfers a Draft invoice to Paraşüt. Sets ParasutId and status = TransferredToParasut.
    /// </summary>
    [HttpPost("{id:guid}/transfer-to-parasut")]
    public async Task<IActionResult> TransferToParasut(Guid id)
    {
        var result = await _mediator.Send(new TransferInvoiceToParasutCommand(id));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<InvoiceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/invoices/import-from-parasut?projectId={projectId}
    /// One-time bulk import of existing Paraşüt invoices into CRM DB.
    /// Fetches all invoices for customers with linked ParasutContactId, skips duplicates.
    /// </summary>
    [HttpPost("import-from-parasut")]
    public async Task<IActionResult> ImportFromParasut([FromQuery] Guid projectId)
    {
        var result = await _mediator.Send(new ImportParasutInvoicesCommand(projectId));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<ImportParasutInvoicesDto>.Ok(result.Value!));
    }
}
