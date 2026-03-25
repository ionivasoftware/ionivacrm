using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetAllProjectTasks;

/// <summary>Handles <see cref="GetAllProjectTasksQuery"/>.</summary>
public class GetAllProjectTasksQueryHandler : IRequestHandler<GetAllProjectTasksQuery, Result<PagedResult<CustomerTaskDto>>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICurrentUserService _currentUser;

    public GetAllProjectTasksQueryHandler(
        ICustomerTaskRepository taskRepository,
        ICurrentUserService currentUser)
    {
        _taskRepository = taskRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerTaskDto>>> Handle(GetAllProjectTasksQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<PagedResult<CustomerTaskDto>>.Failure("Access denied.");

        var (items, totalCount) = await _taskRepository.GetPagedByProjectAsync(
            request.ProjectId,
            request.Status,
            request.Priority,
            request.AssignedUserId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(t => t.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<CustomerTaskDto>(dtos, totalCount, request.Page, request.PageSize);

        return Result<PagedResult<CustomerTaskDto>>.Success(pagedResult);
    }
}
