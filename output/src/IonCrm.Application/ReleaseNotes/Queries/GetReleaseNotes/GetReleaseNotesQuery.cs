using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotes;

/// <summary>Full release-notes payload for the "Yenilikler" screen plus the caller's seen state.</summary>
public record GetReleaseNotesQuery : IRequest<Result<ReleaseNotesDto>>;
