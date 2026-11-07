using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Commands.MarkReleaseNotesSeen;

/// <summary>
/// Records that the caller has looked at the release notes up to <paramref name="ReleaseId"/> (the id the
/// screen displayed at the top). Returns the refreshed badge status.
/// </summary>
public record MarkReleaseNotesSeenCommand(string ReleaseId) : IRequest<Result<ReleaseNotesStatusDto>>;
