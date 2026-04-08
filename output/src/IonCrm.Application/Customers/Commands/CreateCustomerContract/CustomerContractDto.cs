using IonCrm.Domain.Enums;

namespace IonCrm.Application.Customers.Commands.CreateCustomerContract;

/// <summary>
/// DTO returned to the client after a contract is created or fetched.
/// </summary>
public record CustomerContractDto(
    Guid Id,
    Guid CustomerId,
    string Title,
    decimal MonthlyAmount,
    ContractPaymentType PaymentType,
    DateTime StartDate,
    int? DurationMonths,
    DateTime? EndDate,
    ContractStatus Status,
    string? RezervalSubscriptionId,
    string? RezervalPaymentPlanId,
    DateTime? NextInvoiceDate,
    DateTime? LastInvoiceGeneratedDate,
    DateTime CreatedAt);
