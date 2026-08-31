using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.RemoveRole;

/// <summary>
/// Removes a user's role assignment in a specific project (soft-delete of the UserProjectRole).
/// After this the user no longer appears in that project's user list and the project drops out of
/// their token's projectIds on next login. Does not delete the user. SuperAdmin only.
/// </summary>
/// <param name="UserId">The user whose project membership is being removed.</param>
/// <param name="ProjectId">The project to remove the user from.</param>
public record RemoveRoleCommand(Guid UserId, Guid ProjectId) : IRequest<Result>;
