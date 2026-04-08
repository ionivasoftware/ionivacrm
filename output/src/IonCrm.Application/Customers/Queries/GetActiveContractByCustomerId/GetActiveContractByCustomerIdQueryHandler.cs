using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetActiveContractByCustomerId;

/// <summary>Handles <see cref="GetActiveContractByCustomerIdQuery"/>.</summary>
public sealed class GetActiveContractByCustomerIdQueryHandler
    : IRequestHandler<GetActiveContractByCustomerIdQuery, Result<CustomerContractDto?>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerContractRepository _contractRepository;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance of <see cref="GetActiveContractByCustomerIdQueryHandler"/>.</summary>
    public GetActiveContractByCustomerIdQueryHandler(
        ICustomerRepository customerRepository,
        ICustomerContractRepository contractRepository,
        ICurrentUserService currentUser)
    {
        _customerRepository = customerRepository;
        _contractRepository = contractRepository;
        _currentUser        = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerContractDto?>> Handle(
        GetActiveContractByCustomerIdQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerContractDto?>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerContractDto?>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var contract = await _contractRepository.GetActiveByCustomerIdAsync(customer.Id, cancellationToken);

        return Result<CustomerContractDto?>.Success(
            contract is null ? null : CreateCustomerContractCommandHandler.MapToDto(contract));
    }
}
