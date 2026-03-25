using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Opportunities.Commands.UpdateOpportunityStage;

public class UpdateOpportunityStageCommandHandler
    : IRequestHandler<UpdateOpportunityStageCommand, Result<OpportunityDto>>
{
    private readonly IOpportunityRepository _repo;

    public UpdateOpportunityStageCommandHandler(IOpportunityRepository repo) => _repo = repo;

    public async Task<Result<OpportunityDto>> Handle(
        UpdateOpportunityStageCommand request,
        CancellationToken cancellationToken)
    {
        var opportunity = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (opportunity is null)
            return Result<OpportunityDto>.Failure("Opportunity not found.");

        opportunity.Stage = request.Stage;
        opportunity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(opportunity, cancellationToken);
        return Result<OpportunityDto>.Success(opportunity.ToDto());
    }
}
