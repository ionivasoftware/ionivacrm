using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotesStatus;

public class GetReleaseNotesStatusQueryHandler : IRequestHandler<GetReleaseNotesStatusQuery, Result<ReleaseNotesStatusDto>>
{
    private readonly ILiftdeskReleaseNotesClient _client;
    private readonly IUserRepository _users;
    private readonly ICurrentUserService _currentUser;

    public GetReleaseNotesStatusQueryHandler(ILiftdeskReleaseNotesClient client, IUserRepository users, ICurrentUserService currentUser)
    {
        _client = client; _users = users; _currentUser = currentUser;
    }

    public async Task<Result<ReleaseNotesStatusDto>> Handle(GetReleaseNotesStatusQuery request, CancellationToken cancellationToken)
    {
        if (!_client.IsConfigured)
            return Result<ReleaseNotesStatusDto>.Success(ReleaseNotesStatus.Build(null, null, ReleaseNotesStatus.Today(), configured: false));

        var user = _currentUser.IsAuthenticated ? await _users.GetByIdAsync(_currentUser.UserId, cancellationToken) : null;
        var env = await _client.GetAsync(cancellationToken);
        // Liftdesk down → no badge rather than an error toast on every page.
        var data = env.Success ? env.Data : null;
        return Result<ReleaseNotesStatusDto>.Success(ReleaseNotesStatus.Build(data, user?.LastSeenReleaseId, ReleaseNotesStatus.Today()));
    }
}
