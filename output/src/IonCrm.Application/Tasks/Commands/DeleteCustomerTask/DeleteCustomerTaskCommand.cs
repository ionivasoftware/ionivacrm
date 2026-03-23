using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Tasks.Commands.DeleteCustomerTask;

/// <summary>Command to soft-delete a customer task.</summary>
public record DeleteCustomerTaskCommand(Guid Id) : IRequest<Result>;
