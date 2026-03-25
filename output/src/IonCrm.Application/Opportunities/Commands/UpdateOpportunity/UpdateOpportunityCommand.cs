using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Opportunities.Commands.UpdateOpportunity;

public record UpdateOpportunityCommand : IRequest<Result<OpportunityDto>>
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string Title { get; init; } = string.Empty;
    public decimal? Value { get; init; }
    public OpportunityStage Stage { get; init; }
    public int? Probability { get; init; }
    public DateOnly? ExpectedCloseDate { get; init; }
    public Guid? AssignedUserId { get; init; }
}
