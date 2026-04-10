using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Commands.UpdateContractPaymentType;

/// <summary>
/// Changes the payment type of the active contract for a customer.
/// Switching to EFT sets NextInvoiceDate if it was null; switching to
/// CreditCard clears it (iyzico handles card billing, no draft invoices).
/// </summary>
public record UpdateContractPaymentTypeCommand(
    Guid CustomerId,
    ContractPaymentType PaymentType
) : IRequest<Result<CustomerContractDto>>;
