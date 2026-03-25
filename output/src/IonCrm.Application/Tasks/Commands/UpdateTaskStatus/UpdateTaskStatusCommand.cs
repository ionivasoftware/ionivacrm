using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Tasks.Commands.UpdateTaskStatus;

/// <summary>Command to update only the status of a customer task.</summary>
public record UpdateTaskStatusCommand(Guid Id, IonCrm.Domain.Enums.TaskStatus Status) : IRequest<Result<CustomerTaskDto>>;
