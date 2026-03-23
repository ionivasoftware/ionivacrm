using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Tasks.Commands.UpdateCustomerTask;

/// <summary>Command to update an existing customer task.</summary>
public record UpdateCustomerTaskCommand : IRequest<Result<CustomerTaskDto>>
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime? DueDate { get; init; }
    public TaskPriority Priority { get; init; }
    public IonCrm.Domain.Enums.TaskStatus Status { get; init; }
    public Guid? AssignedUserId { get; init; }
}
