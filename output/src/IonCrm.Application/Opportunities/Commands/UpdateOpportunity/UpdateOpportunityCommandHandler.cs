using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Opportunities.Commands.UpdateOpportunity;

public class UpdateOpportunityCommandHandler
    : IRequestHandler<UpdateOpportunityCommand, Result<OpportunityDto>>
{
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateOpportunityCommandHandler> _logger;

    public UpdateOpportunityCommandHandler(
        IOpportunityRepository opportunityRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateOpportunityCommandHandler> logger)
    {
        _opportunityRepository = opportunityRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<OpportunityDto>> Handle(
        UpdateOpportunityCommand request, CancellationToken cancellationToken)
    {
        var opportunity = await _opportunityRepository.GetByIdAsync(request.Id, cancellationToken);
        if (opportunity is null)
            return Result<OpportunityDto>.Failure("Opportunity not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(opportunity.ProjectId))
            return Result<OpportunityDto>.Failure("Access denied.");

        opportunity.Title = request.Title;
        opportunity.Value = request.Value;
        opportunity.Stage = request.Stage;
        opportunity.Probability = request.Probability;
        opportunity.ExpectedCloseDate = request.ExpectedCloseDate;
        opportunity.AssignedUserId = request.AssignedUserId;

        await _opportunityRepository.UpdateAsync(opportunity, cancellationToken);

        _logger.LogInformation("Opportunity {Id} updated", opportunity.Id);

        return Result<OpportunityDto>.Success(opportunity.ToDto());
    }
}
