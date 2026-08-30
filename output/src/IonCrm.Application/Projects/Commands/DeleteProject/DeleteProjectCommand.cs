using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Projects.Commands.DeleteProject;

/// <summary>
/// Archives (soft-deletes) a project: sets <c>IsDeleted=true</c> so it disappears from the
/// project list/switcher (<c>GetAllAsync</c> filters <c>!IsDeleted</c>) while its customers,
/// their archive, and any migration markers stay in the DB. Reversible — clearing IsDeleted
/// restores it. No cascade: child rows are untouched (soft-delete, not hard-delete). SuperAdmin only.
/// </summary>
public record DeleteProjectCommand(Guid Id) : IRequest<Result<string>>;
