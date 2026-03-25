using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetAllContactHistories;

/// <summary>Handles <see cref="GetAllContactHistoriesQuery"/>.</summary>
public class GetAllContactHistoriesQueryHandler
    : IRequestHandler<GetAllContactHistoriesQuery, Result<PagedResult<ContactHistoryDto>>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICurrentUserService _currentUser;

    public GetAllContactHistoriesQueryHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICurrentUserService currentUser)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ContactHistoryDto>>> Handle(
        GetAllContactHistoriesQuery request,
        CancellationToken cancellationToken)
    {
        // If a specific project is requested, verify the user has access to it.
        if (request.ProjectId.HasValue
            && !_currentUser.IsSuperAdmin
            && !_currentUser.ProjectIds.Contains(request.ProjectId.Value))
        {
            return Result<PagedResult<ContactHistoryDto>>.Failure("Access denied to this project.");
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _contactHistoryRepository.GetPagedAllAsync(
            request.ProjectId,
            request.CustomerId,
            request.Type,
            request.FromDate,
            request.ToDate,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(h => h.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<ContactHistoryDto>(dtos, totalCount, page, pageSize);

        return Result<PagedResult<ContactHistoryDto>>.Success(pagedResult);
    }
}
