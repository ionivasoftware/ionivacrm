using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Tasks.Commands.UpdateCustomerTask;

/// <summary>Handles <see cref="UpdateCustomerTaskCommand"/>.</summary>
public class UpdateCustomerTaskCommandHandler : IRequestHandler<UpdateCustomerTaskCommand, Result<CustomerTaskDto>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateCustomerTaskCommandHandler> _logger;

    public UpdateCustomerTaskCommandHandler(
        ICustomerTaskRepository taskRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateCustomerTaskCommandHandler> logger)
    {
        _taskRepository = taskRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerTaskDto>> Handle(UpdateCustomerTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.Id, cancellationToken);
        if (task is null)
            return Result<CustomerTaskDto>.Failure("Task not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(task.ProjectId))
            return Result<CustomerTaskDto>.Failure("Access denied.");

        task.Title = request.Title;
        task.Description = request.Description;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.Status = request.Status;
        task.AssignedUserId = request.AssignedUserId;

        await _taskRepository.UpdateAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} updated", task.Id);

        return Result<CustomerTaskDto>.Success(task.ToDto());
    }
}
