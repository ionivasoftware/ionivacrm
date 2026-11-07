using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotesStatus;

/// <summary>Sidebar badge: latest release + how many the caller has not seen. Never fails on Liftdesk trouble (badge stays quiet).</summary>
public record GetReleaseNotesStatusQuery : IRequest<Result<ReleaseNotesStatusDto>>;
