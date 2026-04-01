using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.CreateInvoice;

/// <summary>Creates a new invoice in the CRM database (Draft status).</summary>
public record CreateInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid CustomerId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? InvoiceSeries { get; init; }
    public int? InvoiceNumber { get; init; }
    public DateTime IssueDate { get; init; }
    public DateTime DueDate { get; init; }
    public string Currency { get; init; } = "TRL";
    public decimal GrossTotal { get; init; }
    public decimal NetTotal { get; init; }
    public string LinesJson { get; init; } = "[]";
}
