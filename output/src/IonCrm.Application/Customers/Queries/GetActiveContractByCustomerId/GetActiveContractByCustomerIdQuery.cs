using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetActiveContractByCustomerId;

/// <summary>
/// Returns the currently <see cref="Domain.Enums.ContractStatus.Active"/> contract for a customer,
/// or <c>null</c> when there is no active contract. Used by the customer detail page to render the
/// "Aktif Sözleşme" summary card and to switch the toolbar button between "Sözleşme Oluştur" and
/// "Sözleşme Yenile".
/// </summary>
public record GetActiveContractByCustomerIdQuery(Guid CustomerId)
    : IRequest<Result<CustomerContractDto?>>;
