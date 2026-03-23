using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.DeleteCustomer;

/// <summary>Handles <see cref="DeleteCustomerCommand"/>.</summary>
public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand, Result>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeleteCustomerCommandHandler> _logger;

    public DeleteCustomerCommandHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<DeleteCustomerCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.Id, cancellationToken);
        if (customer is null)
            return Result.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result.Failure("Access denied to this customer.");

        await _customerRepository.DeleteAsync(customer, cancellationToken);

        _logger.LogInformation("Customer {CustomerId} soft-deleted", customer.Id);

        return Result.Success();
    }
}
