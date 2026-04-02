using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.ExtendEmsExpiration;

/// <summary>
/// Extends the expiration date of an EMS customer via the EMS API.
/// Also updates the local ExpirationDate and optionally creates a Paraşüt draft invoice
/// when durationType is "Months" (1) or "Years" (1).
/// </summary>
public record ExtendEmsExpirationCommand(
    Guid CustomerId,
    string DurationType,   // "Days" | "Months" | "Years"
    int Amount)
    : IRequest<Result<ExtendEmsExpirationDto>>;

/// <summary>Result returned after a successful expiration extension.</summary>
public record ExtendEmsExpirationDto(
    DateTime NewExpirationDate,
    bool ParasutInvoiceCreated,
    string? ParasutInvoiceId,
    string? ParasutInvoiceError = null);
