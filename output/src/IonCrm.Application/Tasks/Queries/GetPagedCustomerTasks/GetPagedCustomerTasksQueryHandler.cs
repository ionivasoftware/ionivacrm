using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetPagedCustomerTasks;

/// <summary>Handles <see cref="GetPagedCustomerTasksQuery"/>.</summary>
public class GetPagedCustomerTasksQueryHandler : IRequestHandler<GetPagedCustomerTasksQuery, Result<PagedResult<CustomerTaskDto>>>
{
    private readonly ICustomerTaskRepository _taskRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetPagedCustomerTasksQueryHandler(
        ICustomerTaskRepository taskRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _taskRepository = taskRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerTaskDto>>> Handle(GetPagedCustomerTasksQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<PagedResult<CustomerTaskDto>>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<PagedResult<CustomerTaskDto>>.Failure("Access denied.");

        var (items, totalCount) = await _taskRepository.GetPagedByCustomerIdAsync(
            request.CustomerId,
            request.Status,
            request.Priority,
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(t => t.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<CustomerTaskDto>(dtos, totalCount, request.Page, request.PageSize);

        return Result<PagedResult<CustomerTaskDto>>.Success(pagedResult);
    }
}
