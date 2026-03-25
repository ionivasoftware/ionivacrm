using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetPagedCustomerTasks;

/// <summary>Query to retrieve a paged list of tasks for a customer.</summary>
public record GetPagedCustomerTasksQuery : IRequest<Result<PagedResult<CustomerTaskDto>>>
{
    public Guid CustomerId { get; init; }
    public IonCrm.Domain.Enums.TaskStatus? Status { get; init; }
    public TaskPriority? Priority { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
