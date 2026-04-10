namespace IonCrm.Application.Common.DTOs;

/// <summary>Represents a user as returned by the API — never includes sensitive fields like PasswordHash.</summary>
public class UserDto
{
    /// <summary>Gets or sets the user's unique ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the combined full name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user has the SuperAdmin flag.</summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>Gets or sets a value indicating whether the user account is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the user's preferred UI theme ("dark" or "light").</summary>
    public string ThemePreference { get; set; } = "dark";

    /// <summary>Gets or sets the UTC timestamp when the account was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the project-role assignments for this user.</summary>
    public List<UserProjectRoleDto> ProjectRoles { get; set; } = new();
}

/// <summary>Represents a user's role assignment within a specific project (tenant).</summary>
public class UserProjectRoleDto
{
    /// <summary>Gets or sets the project (tenant) ID.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the project display name.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Gets or sets the role name within the project.</summary>
    public string Role { get; set; } = string.Empty;
}
