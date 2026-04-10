using IonCrm.Application.Common.DTOs;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Auth;

/// <summary>
/// Internal static helper for mapping <see cref="User"/> domain entities to <see cref="UserDto"/>.
/// Centralises the mapping logic used by multiple command and query handlers.
/// </summary>
internal static class UserMappingHelper
{
    /// <summary>Maps a <see cref="User"/> entity to a <see cref="UserDto"/>.</summary>
    internal static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        FullName = user.FullName,
        IsSuperAdmin = user.IsSuperAdmin,
        IsActive = user.IsActive,
        ThemePreference = user.ThemePreference,
        CreatedAt = user.CreatedAt,
        ProjectRoles = user.UserProjectRoles
            .Select(r => new UserProjectRoleDto
            {
                ProjectId = r.ProjectId,
                ProjectName = r.Project?.Name ?? string.Empty,
                Role = r.Role.ToString()
            })
            .ToList()
    };
}
