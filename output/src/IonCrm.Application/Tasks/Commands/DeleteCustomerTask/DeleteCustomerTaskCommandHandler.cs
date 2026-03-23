using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Tasks.Commands.DeleteCustomerTask;

/// <summary>Handles <see cref="DeleteCustomerTaskCommand"/>.</summary>
public class DeleteCustomerTaskCommandHandler : IRequestHandler<DeleteCustomerTaskCommand, Result>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeleteCustomerTaskCommandHandler> _logger;

    public DeleteCustomerTaskCommandHandler(
        ICustomerTaskRepository taskRepository,
        ICurrentUserService currentUser,
        ILogger<DeleteCustomerTaskCommandHandler> logger)
    {
        _taskRepository = taskRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(DeleteCustomerTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.Id, cancellationToken);
        if (task is null)
            return Result.Failure("Task not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(task.ProjectId))
            return Result.Failure("Access denied.");

        await _taskRepository.DeleteAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} soft-deleted", task.Id);

        return Result.Success();
    }
}
