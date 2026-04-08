using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Commands.CreateCustomerContract;

/// <summary>
/// Creates (or renews) a recurring monthly subscription contract for a Rezerval customer.
/// Calls the Rezerval subscription endpoint to create an iyzico subscription + payment plan
/// on the Rezerval side, then stores a local <c>CustomerContract</c> with the returned refs.
/// If the customer already has an Active contract it is marked Completed (renewal semantics).
/// For EFT/Wire contracts, the background sync job will auto-create monthly draft invoices.
/// </summary>
public record CreateCustomerContractCommand(
    Guid CustomerId,
    decimal MonthlyAmount,
    ContractPaymentType PaymentType,
    DateTime StartDate,           // date-only; UTC midnight of the start date
    int? DurationMonths)          // null = indefinite
    : IRequest<Result<CustomerContractDto>>;
