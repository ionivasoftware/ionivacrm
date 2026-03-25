using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Application.Tasks.Mappings;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerWithDetails;

/// <summary>Handles <see cref="GetCustomerWithDetailsQuery"/>.</summary>
public class GetCustomerWithDetailsQueryHandler
    : IRequestHandler<GetCustomerWithDetailsQuery, Result<CustomerWithDetailsDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCustomerWithDetailsQueryHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerWithDetailsDto>> Handle(
        GetCustomerWithDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetWithDetailsAsync(request.Id, cancellationToken);
        if (customer is null)
            return Result<CustomerWithDetailsDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerWithDetailsDto>.Failure("Customer not found.");

        var openTasks = customer.Tasks
            .Where(t => t.Status == IonCrm.Domain.Enums.TaskStatus.Todo
                     || t.Status == IonCrm.Domain.Enums.TaskStatus.InProgress)
            .OrderBy(t => t.DueDate)
            .Select(t => t.ToDto())
            .ToList();

        var recentHistories = customer.ContactHistories
            .OrderByDescending(h => h.ContactedAt)
            .Take(5)
            .Select(h => h.ToDto())
            .ToList();

        var dto = new CustomerWithDetailsDto
        {
            Id = customer.Id,
            ProjectId = customer.ProjectId,
            Code = customer.Code,
            CompanyName = customer.CompanyName,
            ContactName = customer.ContactName,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            TaxNumber = customer.TaxNumber,
            TaxUnit = customer.TaxUnit,
            Status = customer.Status,
            Segment = customer.Segment,
            Label = customer.Label,
            AssignedUserId = customer.AssignedUserId,
            AssignedUserName = customer.AssignedUser is not null
                ? $"{customer.AssignedUser.FirstName} {customer.AssignedUser.LastName}".Trim()
                : null,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            TotalContactHistories = customer.ContactHistories.Count,
            TotalTasks = customer.Tasks.Count,
            OpenTasksCount = openTasks.Count,
            RecentContactHistories = recentHistories,
            OpenTasks = openTasks
        };

        return Result<CustomerWithDetailsDto>.Success(dto);
    }
}
