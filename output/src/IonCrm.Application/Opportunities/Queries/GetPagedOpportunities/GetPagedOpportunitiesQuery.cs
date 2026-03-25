using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Opportunities.Queries.GetPagedOpportunities;

public record GetPagedOpportunitiesQuery : IRequest<Result<PagedResult<OpportunityDto>>>
{
    public Guid CustomerId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
