using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetAllProjectTasks;

/// <summary>Query to retrieve a paged list of tasks across a project.</summary>
public record GetAllProjectTasksQuery : IRequest<Result<PagedResult<CustomerTaskDto>>>
{
    public Guid ProjectId { get; init; }
    public IonCrm.Domain.Enums.TaskStatus? Status { get; init; }
    public TaskPriority? Priority { get; init; }
    public Guid? AssignedUserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
