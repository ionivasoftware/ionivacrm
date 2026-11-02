using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;

/// <summary>Handles <see cref="UpdateContactHistoryCommand"/>.</summary>
public class UpdateContactHistoryCommandHandler : IRequestHandler<UpdateContactHistoryCommand, Result<ContactHistoryDto>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateContactHistoryCommandHandler> _logger;

    public UpdateContactHistoryCommandHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateContactHistoryCommandHandler> logger)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ContactHistoryDto>> Handle(UpdateContactHistoryCommand request, CancellationToken cancellationToken)
    {
        var history = await _contactHistoryRepository.GetByIdAsync(request.Id, cancellationToken);
        if (history is null)
            return Result<ContactHistoryDto>.Failure("Contact history record not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(history.ProjectId))
            return Result<ContactHistoryDto>.Failure("Access denied.");

        history.Type = request.Type;
        history.Subject = request.Subject;
        history.Content = request.Content;
        history.Outcome = request.Outcome;
        history.ContactedAt = request.ContactedAt;
        history.Purpose = request.Purpose;
        history.Reached = request.Reached;
        history.SatisfactionScore = request.SatisfactionScore;
        history.NextContactAt = request.NextContactAt;
        history.UsesCompetitor = request.UsesCompetitor ?? false;
        history.CompetitorId = history.UsesCompetitor ? request.CompetitorId : null;

        await _contactHistoryRepository.UpdateAsync(history, cancellationToken);

        if (history.UsesCompetitor)
        {
            var customer = await _customerRepository.GetByIdAsync(history.CustomerId, cancellationToken);
            if (customer is not null && CompetitorMirror.Apply(customer, history))
                await _customerRepository.UpdateAsync(customer, cancellationToken);
        }

        _logger.LogInformation("ContactHistory {Id} updated", history.Id);

        return Result<ContactHistoryDto>.Success(history.ToDto());
    }
}
