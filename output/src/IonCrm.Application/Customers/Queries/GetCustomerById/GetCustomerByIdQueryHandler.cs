using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerById;

/// <summary>Handles <see cref="GetCustomerByIdQuery"/>.</summary>
public class GetCustomerByIdQueryHandler : IRequestHandler<GetCustomerByIdQuery, Result<CustomerDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCustomerByIdQueryHandler(ICustomerRepository customerRepository, ICurrentUserService currentUser)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.Id, cancellationToken);
        if (customer is null)
            return Result<CustomerDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerDto>.Failure("Customer not found.");

        return Result<CustomerDto>.Success(customer.ToDto());
    }
}
