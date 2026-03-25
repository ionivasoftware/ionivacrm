using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetCustomerTaskById;

/// <summary>Handles <see cref="GetCustomerTaskByIdQuery"/>.</summary>
public class GetCustomerTaskByIdQueryHandler : IRequestHandler<GetCustomerTaskByIdQuery, Result<CustomerTaskDto>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCustomerTaskByIdQueryHandler(
        ICustomerTaskRepository taskRepository,
        ICurrentUserService currentUser)
    {
        _taskRepository = taskRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerTaskDto>> Handle(GetCustomerTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.Id, cancellationToken);
        if (task is null)
            return Result<CustomerTaskDto>.Failure("Task not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(task.ProjectId))
            return Result<CustomerTaskDto>.Failure("Access denied to this task.");

        return Result<CustomerTaskDto>.Success(task.ToDto());
    }
}
