using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Opportunities.Queries.GetAllProjectOpportunities;

public class GetAllProjectOpportunitiesQueryHandler
    : IRequestHandler<GetAllProjectOpportunitiesQuery, Result<PagedResult<OpportunityDto>>>
{
    private readonly IOpportunityRepository _repo;
    private readonly ICurrentUserService _currentUser;

    public GetAllProjectOpportunitiesQueryHandler(IOpportunityRepository repo, ICurrentUserService currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<OpportunityDto>>> Handle(
        GetAllProjectOpportunitiesQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<PagedResult<OpportunityDto>>.Failure("Access denied.");

        var (items, totalCount) = await _repo.GetPagedByProjectAsync(
            request.ProjectId,
            request.Stage,
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(o => o.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<OpportunityDto>(dtos, totalCount, request.Page, request.PageSize);
        return Result<PagedResult<OpportunityDto>>.Success(pagedResult);
    }
}
