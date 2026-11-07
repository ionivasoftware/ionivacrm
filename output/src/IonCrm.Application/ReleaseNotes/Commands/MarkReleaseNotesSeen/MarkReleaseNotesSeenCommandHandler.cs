using System.Text.RegularExpressions;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Commands.MarkReleaseNotesSeen;

public class MarkReleaseNotesSeenCommandHandler : IRequestHandler<MarkReleaseNotesSeenCommand, Result<ReleaseNotesStatusDto>>
{
    /// <summary>Release ids are EMS-made slugs ("2026-10-31_2026-11-07"); anything else is rejected before touching the row.</summary>
    public static readonly Regex IdPattern = new(@"^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);

    private readonly ILiftdeskReleaseNotesClient _client;
    private readonly IUserRepository _users;
    private readonly ICurrentUserService _currentUser;

    public MarkReleaseNotesSeenCommandHandler(ILiftdeskReleaseNotesClient client, IUserRepository users, ICurrentUserService currentUser)
    {
        _client = client; _users = users; _currentUser = currentUser;
    }

    public async Task<Result<ReleaseNotesStatusDto>> Handle(MarkReleaseNotesSeenCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ReleaseNotesStatusDto>.Failure("Kimlik doğrulaması gereklidir.");

        var id = request.ReleaseId?.Trim() ?? string.Empty;
        if (!IdPattern.IsMatch(id))
            return Result<ReleaseNotesStatusDto>.Failure("Geçersiz sürüm kimliği.");

        var user = await _users.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null)
            return Result<ReleaseNotesStatusDto>.Failure("Kullanıcı bulunamadı.");

        if (!string.Equals(user.LastSeenReleaseId, id, StringComparison.Ordinal))
        {
            user.LastSeenReleaseId = id;
            await _users.UpdateAsync(user, cancellationToken);
        }

        if (!_client.IsConfigured)
            return Result<ReleaseNotesStatusDto>.Success(ReleaseNotesStatus.Build(null, id, ReleaseNotesStatus.Today(), configured: false));

        var env = await _client.GetAsync(cancellationToken);
        return Result<ReleaseNotesStatusDto>.Success(ReleaseNotesStatus.Build(env.Success ? env.Data : null, id, ReleaseNotesStatus.Today()));
    }
}
