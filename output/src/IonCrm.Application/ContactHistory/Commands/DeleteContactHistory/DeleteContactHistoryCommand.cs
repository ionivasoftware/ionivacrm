using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ContactHistory.Commands.DeleteContactHistory;

/// <summary>Command to soft-delete a contact history record.</summary>
public record DeleteContactHistoryCommand(Guid Id) : IRequest<Result>;
