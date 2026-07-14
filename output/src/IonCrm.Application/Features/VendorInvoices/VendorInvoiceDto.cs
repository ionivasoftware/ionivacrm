using IonCrm.Domain.Entities;

namespace IonCrm.Application.Features.VendorInvoices;

/// <summary>Flattened view of a <see cref="VendorInvoice"/> for the reconciliation screen.</summary>
public record VendorInvoiceDto(
    Guid Id,
    string Provider,
    int PeriodYear,
    int PeriodMonth,
    string BillingType,
    string Status,
    decimal? ExpectedAmount,
    decimal? ReceivedAmount,
    string? Currency,
    string? InvoiceNumber,
    string? PdfUrl,
    int DueDay,
    DateTime DueDate,
    DateTime? ExpectedOn,
    DateTime? ReceivedOn,
    DateTime? AlertedOn,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Maps <see cref="VendorInvoice"/> entities to <see cref="VendorInvoiceDto"/>.</summary>
public static class VendorInvoiceMappings
{
    /// <summary>Projects a single entity to its DTO.</summary>
    public static VendorInvoiceDto ToDto(this VendorInvoice e) => new(
        e.Id,
        e.Provider,
        e.PeriodYear,
        e.PeriodMonth,
        e.BillingType.ToString(),
        e.Status.ToString(),
        e.ExpectedAmount,
        e.ReceivedAmount,
        e.Currency,
        e.InvoiceNumber,
        e.PdfUrl,
        e.DueDay,
        e.DueDate(),
        e.ExpectedOn,
        e.ReceivedOn,
        e.AlertedOn,
        e.Notes,
        e.CreatedAt,
        e.UpdatedAt);
}
