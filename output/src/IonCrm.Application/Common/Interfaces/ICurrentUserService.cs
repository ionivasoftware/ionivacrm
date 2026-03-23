namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Provides access to the currently authenticated user's identity and tenant context.
/// Populated by TenantMiddleware from JWT claims on each HTTP request.
/// SuperAdmin users have IsSuperAdmin=true and bypass all tenant (ProjectId) filters.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Gets the authenticated user's ID, or <see cref="Guid.Empty"/> if anonymous.</summary>
    Guid UserId { get; }

    /// <summary>Gets the authenticated user's email, or empty string if anonymous.</summary>
    string Email { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is a SuperAdmin.
    /// SuperAdmins bypass all tenant filters and can see all project data.
    /// </summary>
    bool IsSuperAdmin { get; }

    /// <summary>Gets the list of project IDs the current user is a member of.</summary>
    List<Guid> ProjectIds { get; }

    /// <summary>Gets a value indicating whether the current request is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Returns the role name for the user within the specified project,
    /// or null if the user has no role in that project.
    /// </summary>
    string? GetRoleForProject(Guid projectId);
}
