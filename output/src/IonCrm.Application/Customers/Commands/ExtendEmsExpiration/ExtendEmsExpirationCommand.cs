using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.ExtendEmsExpiration;

/// <summary>
/// Extends the expiration date of an EMS customer via the EMS API.
/// Also updates the local ExpirationDate and creates a local CRM draft invoice
/// when durationType is "Months" (1) or "Years" (1).
/// </summary>
public record ExtendEmsExpirationCommand(
    Guid CustomerId,
    string DurationType,   // "Days" | "Months" | "Years"
    int Amount,
    /// <summary>İskonto tutarı/oranı. 0 = iskonto yok.</summary>
    decimal DiscountValue = 0m,
    /// <summary>"percentage" (varsayılan) | "amount" — fatura satırıyla aynı sözleşme.</summary>
    string DiscountType = "percentage")
    : IRequest<Result<ExtendEmsExpirationDto>>;

/// <summary>Result returned after a successful expiration extension.</summary>
public record ExtendEmsExpirationDto(
    DateTime NewExpirationDate,
    bool InvoiceCreated,
    Guid? InvoiceId,
    string? InvoiceError = null);
