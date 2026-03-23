using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Auth.Commands.AssignRole;

/// <summary>
/// Command to assign (or update) a user's role within a specific project (tenant).
/// Restricted to SuperAdmin or ProjectAdmin callers.
/// If the user already has a role in the project it is updated; otherwise a new assignment is created.
/// </summary>
/// <param name="UserId">The ID of the user to assign a role to.</param>
/// <param name="ProjectId">The target project (tenant) ID.</param>
/// <param name="Role">The role to assign.</param>
public record AssignRoleCommand(
    Guid UserId,
    Guid ProjectId,
    UserRole Role) : IRequest<Result>;
