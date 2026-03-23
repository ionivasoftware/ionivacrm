using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.CreateCustomer;

/// <summary>Handles <see cref="CreateCustomerCommand"/>.</summary>
public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Result<CustomerDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateCustomerCommandHandler> _logger;

    public CreateCustomerCommandHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<CreateCustomerCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<CustomerDto>.Failure("Access denied to this project.");

        var customer = new Customer
        {
            ProjectId = request.ProjectId,
            Code = request.Code,
            CompanyName = request.CompanyName,
            ContactName = request.ContactName,
            Email = request.Email?.ToLowerInvariant().Trim(),
            Phone = request.Phone,
            Address = request.Address,
            TaxNumber = request.TaxNumber,
            TaxUnit = request.TaxUnit,
            Status = request.Status,
            Segment = request.Segment,
            AssignedUserId = request.AssignedUserId
        };

        await _customerRepository.AddAsync(customer, cancellationToken);

        _logger.LogInformation("Customer {CustomerId} created in project {ProjectId}", customer.Id, customer.ProjectId);

        return Result<CustomerDto>.Success(customer.ToDto());
    }
}
