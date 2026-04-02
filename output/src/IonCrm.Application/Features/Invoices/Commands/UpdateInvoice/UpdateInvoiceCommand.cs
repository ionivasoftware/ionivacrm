using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.UpdateInvoice;

/// <summary>
/// Updates an existing Draft invoice in the CRM database.
/// Only invoices with <c>InvoiceStatus.Draft</c> can be updated.
/// NetTotal and GrossTotal are recomputed server-side from the provided LinesJson,
/// taking per-line discounts into account.
/// </summary>
public record UpdateInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid InvoiceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? InvoiceSeries { get; init; }
    public int? InvoiceNumber { get; init; }
    public DateTime IssueDate { get; init; }
    public DateTime DueDate { get; init; }
    public string Currency { get; init; } = "TRL";

    /// <summary>
    /// JSON array of line items.
    /// NetTotal and GrossTotal will be computed from this by the handler.
    /// </summary>
    public string LinesJson { get; init; } = "[]";
}
