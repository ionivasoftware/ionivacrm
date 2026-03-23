using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Many-to-many join entity — assigns a User a Role within a specific Project (tenant).
/// One user can have different roles in different projects.
/// </summary>
public class UserProjectRole : BaseEntity
{
    /// <summary>Gets or sets the user's identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the project (tenant) identifier.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the role this user holds within the project.</summary>
    public UserRole Role { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the user.</summary>
    public User User { get; set; } = null!;

    /// <summary>Gets or sets the project.</summary>
    public Project Project { get; set; } = null!;
}
