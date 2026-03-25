using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Tasks.Commands.UpdateTaskStatus;

/// <summary>Handles <see cref="UpdateTaskStatusCommand"/>.</summary>
public class UpdateTaskStatusCommandHandler : IRequestHandler<UpdateTaskStatusCommand, Result<CustomerTaskDto>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateTaskStatusCommandHandler> _logger;

    public UpdateTaskStatusCommandHandler(
        ICustomerTaskRepository taskRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateTaskStatusCommandHandler> logger)
    {
        _taskRepository = taskRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerTaskDto>> Handle(UpdateTaskStatusCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.Id, cancellationToken);
        if (task is null)
            return Result<CustomerTaskDto>.Failure("Task not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(task.ProjectId))
            return Result<CustomerTaskDto>.Failure("Access denied to this task.");

        task.Status = request.Status;

        await _taskRepository.UpdateAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} status updated to {Status}", task.Id, request.Status);

        return Result<CustomerTaskDto>.Success(task.ToDto());
    }
}
