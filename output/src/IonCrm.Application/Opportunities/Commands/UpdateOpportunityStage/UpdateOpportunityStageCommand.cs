using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Opportunities.Commands.UpdateOpportunityStage;

public record UpdateOpportunityStageCommand : IRequest<Result<OpportunityDto>>
{
    public Guid Id { get; init; }
    public OpportunityStage Stage { get; init; }
}
