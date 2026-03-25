using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Opportunities.Queries.GetAllProjectOpportunities;

public record GetAllProjectOpportunitiesQuery : IRequest<Result<PagedResult<OpportunityDto>>>
{
    public Guid ProjectId { get; init; }
    public OpportunityStage? Stage { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 200;
}
