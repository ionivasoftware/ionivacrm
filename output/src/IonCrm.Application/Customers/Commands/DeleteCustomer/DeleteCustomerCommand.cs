using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.DeleteCustomer;

/// <summary>Command to soft-delete a customer.</summary>
public record DeleteCustomerCommand(Guid Id) : IRequest<Result>;
