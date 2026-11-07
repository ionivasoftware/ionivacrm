using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotes;

public class GetReleaseNotesQueryHandler : IRequestHandler<GetReleaseNotesQuery, Result<ReleaseNotesDto>>
{
    public const string NotConfiguredMessage = "Liftdesk API anahtarı yapılandırılmamış.";

    private readonly ILiftdeskReleaseNotesClient _client;
    private readonly IUserRepository _users;
    private readonly ICurrentUserService _currentUser;

    public GetReleaseNotesQueryHandler(ILiftdeskReleaseNotesClient client, IUserRepository users, ICurrentUserService currentUser)
    {
        _client = client; _users = users; _currentUser = currentUser;
    }

    public async Task<Result<ReleaseNotesDto>> Handle(GetReleaseNotesQuery request, CancellationToken cancellationToken)
    {
        if (!_client.IsConfigured)
            return Result<ReleaseNotesDto>.Failure(NotConfiguredMessage);

        var env = await _client.GetAsync(cancellationToken);
        if (!env.Success || env.Data is null)
            return Result<ReleaseNotesDto>.Failure(env.Message ?? "Sürüm notları alınamadı.");

        var user = _currentUser.IsAuthenticated ? await _users.GetByIdAsync(_currentUser.UserId, cancellationToken) : null;
        var lastSeen = user?.LastSeenReleaseId;
        var releases = ReleaseNotesStatus.Usable(env.Data);

        return Result<ReleaseNotesDto>.Success(new ReleaseNotesDto(
            string.IsNullOrWhiteSpace(env.Data.Language) ? "tr" : env.Data.Language,
            env.Data.GeneratedAt,
            releases,
            lastSeen,
            ReleaseNotesStatus.UnseenCount(releases, lastSeen, ReleaseNotesStatus.Today())));
    }
}
