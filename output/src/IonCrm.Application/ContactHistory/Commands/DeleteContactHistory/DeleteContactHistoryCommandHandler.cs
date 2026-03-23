using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.ContactHistory.Commands.DeleteContactHistory;

/// <summary>Handles <see cref="DeleteContactHistoryCommand"/>.</summary>
public class DeleteContactHistoryCommandHandler : IRequestHandler<DeleteContactHistoryCommand, Result>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeleteContactHistoryCommandHandler> _logger;

    public DeleteContactHistoryCommandHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICurrentUserService currentUser,
        ILogger<DeleteContactHistoryCommandHandler> logger)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(DeleteContactHistoryCommand request, CancellationToken cancellationToken)
    {
        var history = await _contactHistoryRepository.GetByIdAsync(request.Id, cancellationToken);
        if (history is null)
            return Result.Failure("Contact history record not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(history.ProjectId))
            return Result.Failure("Access denied.");

        await _contactHistoryRepository.DeleteAsync(history, cancellationToken);

        _logger.LogInformation("ContactHistory {Id} soft-deleted", history.Id);

        return Result.Success();
    }
}
