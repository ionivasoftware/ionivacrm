using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a system user. Users can belong to multiple projects with different roles.
/// SuperAdmin users bypass all tenant filters.
/// </summary>
public class User : BaseEntity
{
    /// <summary>Gets or sets the user's unique email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the bcrypt-hashed password (cost factor 12).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this user is a SuperAdmin.
    /// SuperAdmin users can see all projects and all data, bypassing tenant filters.
    /// </summary>
    public bool IsSuperAdmin { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether this user account is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the user's preferred UI theme ("dark" or "light"). Defaults to "dark".</summary>
    public string ThemePreference { get; set; } = "dark";

    /// <summary>Gets the user's full name.</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation properties
    /// <summary>Gets or sets the project role assignments for this user.</summary>
    public ICollection<UserProjectRole> UserProjectRoles { get; set; } = new List<UserProjectRole>();

    /// <summary>Gets or sets the refresh tokens for this user.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
