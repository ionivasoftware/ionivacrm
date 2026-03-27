using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.CreateSalesInvoice;

/// <summary>Creates a sales invoice (satış faturası) in Paraşüt.</summary>
public record CreateParasutSalesInvoiceCommand(
    Guid   ProjectId,
    /// <summary>Paraşüt contact ID (cari ID). Null if creating an open (unlinked) invoice.</summary>
    string? ParasutContactId,
    string  IssueDate,       // yyyy-MM-dd
    string  DueDate,         // yyyy-MM-dd
    string  Currency,        // "TRL" | "USD" | "EUR"
    string? Description,
    string? InvoiceSeries,
    int?    InvoiceId,
    List<InvoiceLineItem> Lines
) : IRequest<Result<CreateParasutSalesInvoiceDto>>;

/// <summary>A single line item on the invoice.</summary>
public record InvoiceLineItem(
    string?  Description,
    decimal  Quantity,
    decimal  UnitPrice,
    int      VatRate,         // 0, 1, 8, 10, 20
    decimal  DiscountValue = 0,
    string   DiscountType = "percentage",
    string?  Unit = "Adet"
);

/// <summary>Response DTO after creating a Paraşüt sales invoice.</summary>
public record CreateParasutSalesInvoiceDto(
    string  ParasutInvoiceId,
    string  IssueDate,
    string  DueDate,
    decimal GrossTotal,
    decimal NetTotal,
    string  Currency
);
