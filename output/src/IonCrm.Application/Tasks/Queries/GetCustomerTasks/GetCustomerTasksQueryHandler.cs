using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetCustomerTasks;

/// <summary>Handles <see cref="GetCustomerTasksQuery"/>.</summary>
public class GetCustomerTasksQueryHandler : IRequestHandler<GetCustomerTasksQuery, Result<IReadOnlyList<CustomerTaskDto>>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCustomerTasksQueryHandler(
        ICustomerTaskRepository taskRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _taskRepository = taskRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CustomerTaskDto>>> Handle(GetCustomerTasksQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<IReadOnlyList<CustomerTaskDto>>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<IReadOnlyList<CustomerTaskDto>>.Failure("Access denied.");

        var tasks = await _taskRepository.GetByCustomerIdAsync(request.CustomerId, cancellationToken);
        var dtos = (IReadOnlyList<CustomerTaskDto>)tasks.Select(t => t.ToDto()).ToList().AsReadOnly();

        return Result<IReadOnlyList<CustomerTaskDto>>.Success(dtos);
    }
}
