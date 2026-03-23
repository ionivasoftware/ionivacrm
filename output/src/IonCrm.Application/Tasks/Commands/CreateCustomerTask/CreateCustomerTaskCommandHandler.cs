using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Tasks.Commands.CreateCustomerTask;

/// <summary>Handles <see cref="CreateCustomerTaskCommand"/>.</summary>
public class CreateCustomerTaskCommandHandler : IRequestHandler<CreateCustomerTaskCommand, Result<CustomerTaskDto>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateCustomerTaskCommandHandler> _logger;

    public CreateCustomerTaskCommandHandler(
        ICustomerTaskRepository taskRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<CreateCustomerTaskCommandHandler> logger)
    {
        _taskRepository = taskRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerTaskDto>> Handle(CreateCustomerTaskCommand request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerTaskDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerTaskDto>.Failure("Access denied to this customer.");

        var task = new CustomerTask
        {
            CustomerId = request.CustomerId,
            ProjectId = customer.ProjectId,
            Title = request.Title,
            Description = request.Description,
            DueDate = request.DueDate,
            Priority = request.Priority,
            Status = IonCrm.Domain.Enums.TaskStatus.Todo,
            AssignedUserId = request.AssignedUserId
        };

        await _taskRepository.AddAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} created for customer {CustomerId}", task.Id, task.CustomerId);

        return Result<CustomerTaskDto>.Success(task.ToDto());
    }
}
