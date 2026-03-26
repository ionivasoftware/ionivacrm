using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Opportunities.Commands.UpdateOpportunityStage;

public class UpdateOpportunityStageCommandHandler
    : IRequestHandler<UpdateOpportunityStageCommand, Result<OpportunityDto>>
{
    private readonly IOpportunityRepository _repo;
    private readonly IContactHistoryRepository _contactHistoryRepo;
    private readonly ICurrentUserService _currentUser;

    public UpdateOpportunityStageCommandHandler(
        IOpportunityRepository repo,
        IContactHistoryRepository contactHistoryRepo,
        ICurrentUserService currentUser)
    {
        _repo = repo;
        _contactHistoryRepo = contactHistoryRepo;
        _currentUser = currentUser;
    }

    private static readonly Dictionary<OpportunityStage, string> StageLabels = new()
    {
        { OpportunityStage.YeniArama,  "Yeni Arama" },
        { OpportunityStage.Potansiyel, "Potansiyel" },
        { OpportunityStage.Demo,       "Demo" },
        { OpportunityStage.Musteri,    "Müşteri" },
        { OpportunityStage.Kayip,      "Kayıp" },
    };

    public async Task<Result<OpportunityDto>> Handle(
        UpdateOpportunityStageCommand request,
        CancellationToken cancellationToken)
    {
        var opportunity = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (opportunity is null)
            return Result<OpportunityDto>.Failure("Opportunity not found.");

        var previousStage = opportunity.Stage;
        opportunity.Stage = request.Stage;
        opportunity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(opportunity, cancellationToken);

        // Log stage change as a contact history Note
        if (previousStage != request.Stage)
        {
            var prevLabel = StageLabels.GetValueOrDefault(previousStage, previousStage.ToString());
            var newLabel  = StageLabels.GetValueOrDefault(request.Stage, request.Stage.ToString());

            var history = new IonCrm.Domain.Entities.ContactHistory
            {
                CustomerId      = opportunity.CustomerId,
                ProjectId       = opportunity.ProjectId,
                Type            = ContactType.Note,
                Subject         = $"Pipeline aşaması değişti: {prevLabel} → {newLabel}",
                Content         = $"Fırsat: {opportunity.Title}",
                ContactedAt     = DateTime.UtcNow,
                CreatedByUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId,
            };

            await _contactHistoryRepo.AddAsync(history, cancellationToken);
        }

        return Result<OpportunityDto>.Success(opportunity.ToDto());
    }
}
