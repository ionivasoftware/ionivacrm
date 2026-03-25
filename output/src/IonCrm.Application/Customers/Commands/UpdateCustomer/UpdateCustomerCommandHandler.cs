using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.UpdateCustomer;

/// <summary>Handles <see cref="UpdateCustomerCommand"/>.</summary>
public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, Result<CustomerDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateCustomerCommandHandler> _logger;

    public UpdateCustomerCommandHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateCustomerCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.Id, cancellationToken);
        if (customer is null)
            return Result<CustomerDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerDto>.Failure("Access denied to this customer.");

        customer.Code = request.Code;
        customer.CompanyName = request.CompanyName;
        customer.ContactName = request.ContactName;
        customer.Email = request.Email?.ToLowerInvariant().Trim();
        customer.Phone = request.Phone;
        customer.Address = request.Address;
        customer.TaxNumber = request.TaxNumber;
        customer.TaxUnit = request.TaxUnit;
        customer.Status = request.Status;
        customer.Segment = request.Segment;
        customer.Label = request.Label;
        customer.AssignedUserId = request.AssignedUserId;

        await _customerRepository.UpdateAsync(customer, cancellationToken);

        _logger.LogInformation("Customer {CustomerId} updated", customer.Id);

        return Result<CustomerDto>.Success(customer.ToDto());
    }
}
