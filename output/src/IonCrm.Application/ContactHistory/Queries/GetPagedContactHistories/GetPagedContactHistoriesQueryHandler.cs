using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetPagedContactHistories;

/// <summary>Handles <see cref="GetPagedContactHistoriesQuery"/>.</summary>
public class GetPagedContactHistoriesQueryHandler : IRequestHandler<GetPagedContactHistoriesQuery, Result<PagedResult<ContactHistoryDto>>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetPagedContactHistoriesQueryHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ContactHistoryDto>>> Handle(GetPagedContactHistoriesQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<PagedResult<ContactHistoryDto>>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<PagedResult<ContactHistoryDto>>.Failure("Access denied.");

        var (items, totalCount) = await _contactHistoryRepository.GetPagedByCustomerIdAsync(
            request.CustomerId,
            request.Type,
            request.FromDate,
            request.ToDate,
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(h => h.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<ContactHistoryDto>(dtos, totalCount, request.Page, request.PageSize);

        return Result<PagedResult<ContactHistoryDto>>.Success(pagedResult);
    }
}
