using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetContactHistoryById;

/// <summary>Handles <see cref="GetContactHistoryByIdQuery"/>.</summary>
public class GetContactHistoryByIdQueryHandler : IRequestHandler<GetContactHistoryByIdQuery, Result<ContactHistoryDto>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICurrentUserService _currentUser;

    public GetContactHistoryByIdQueryHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICurrentUserService currentUser)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<ContactHistoryDto>> Handle(GetContactHistoryByIdQuery request, CancellationToken cancellationToken)
    {
        var history = await _contactHistoryRepository.GetByIdAsync(request.Id, cancellationToken);
        if (history is null)
            return Result<ContactHistoryDto>.Failure("Contact history not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(history.ProjectId))
            return Result<ContactHistoryDto>.Failure("Access denied to this contact history.");

        return Result<ContactHistoryDto>.Success(history.ToDto());
    }
}
