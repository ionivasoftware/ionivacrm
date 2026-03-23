using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetContactHistories;

/// <summary>Handles <see cref="GetContactHistoriesQuery"/>.</summary>
public class GetContactHistoriesQueryHandler : IRequestHandler<GetContactHistoriesQuery, Result<IReadOnlyList<ContactHistoryDto>>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetContactHistoriesQueryHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContactHistoryDto>>> Handle(GetContactHistoriesQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<IReadOnlyList<ContactHistoryDto>>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<IReadOnlyList<ContactHistoryDto>>.Failure("Access denied.");

        var histories = await _contactHistoryRepository.GetByCustomerIdAsync(request.CustomerId, cancellationToken);
        var dtos = (IReadOnlyList<ContactHistoryDto>)histories.Select(h => h.ToDto()).ToList().AsReadOnly();

        return Result<IReadOnlyList<ContactHistoryDto>>.Success(dtos);
    }
}
