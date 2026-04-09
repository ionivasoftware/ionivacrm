using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using MediatR;

namespace IonCrm.Application.Customers.Commands.CancelCustomerContract;

/// <summary>
/// Cancels the active recurring subscription contract for a Rezerval customer.
/// Calls the Rezerval cancel endpoint (which deletes the iyzico pricing plan + product
/// in the right order and tolerates iyzico-side failures via warnings) and then marks
/// the local <c>CustomerContract</c> row as Cancelled.
/// </summary>
public record CancelCustomerContractCommand(Guid CustomerId)
    : IRequest<Result<CancelCustomerContractDto>>;

/// <summary>
/// Result of a cancel operation. Contains the updated contract DTO and any iyzico-side
/// warnings that the Rezerval API surfaced (e.g. plan already deleted, product missing).
/// Local cleanup ALWAYS runs even when warnings are present.
/// </summary>
public record CancelCustomerContractDto(
    CustomerContractDto Contract,
    List<string> IyzicoWarnings);
