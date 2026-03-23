using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Tasks.Commands.CreateCustomerTask;

/// <summary>Command to create a new task for a customer.</summary>
public record CreateCustomerTaskCommand : IRequest<Result<CustomerTaskDto>>
{
    public Guid CustomerId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime? DueDate { get; init; }
    public TaskPriority Priority { get; init; } = TaskPriority.Medium;
    public Guid? AssignedUserId { get; init; }
}
