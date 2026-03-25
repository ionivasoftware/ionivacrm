using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;

/// <summary>Handles <see cref="ConvertLeadToCustomerCommand"/>.</summary>
public class ConvertLeadToCustomerCommandHandler
    : IRequestHandler<ConvertLeadToCustomerCommand, Result<CustomerDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ConvertLeadToCustomerCommandHandler> _logger;

    public ConvertLeadToCustomerCommandHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<ConvertLeadToCustomerCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> Handle(
        ConvertLeadToCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerDto>.Failure("Access denied to this customer.");

        if (customer.Status != CustomerStatus.Lead)
            return Result<CustomerDto>.Failure(
                $"Customer is already converted. Current status: {customer.Status}.");

        customer.Status = CustomerStatus.Active;

        await _customerRepository.UpdateAsync(customer, cancellationToken);

        _logger.LogInformation(
            "Customer {CustomerId} converted from Lead to Active by user {UserId}",
            customer.Id, _currentUser.UserId);

        return Result<CustomerDto>.Success(customer.ToDto());
    }
}
