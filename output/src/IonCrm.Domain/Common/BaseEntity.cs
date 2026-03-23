namespace IonCrm.Domain.Common;

/// <summary>
/// Base entity class — all domain entities inherit from this.
/// Provides Id (Guid), audit timestamps, and soft-delete support per CLAUDE.md rules.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Gets or sets the unique identifier (UUID).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the UTC timestamp when this entity was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC timestamp when this entity was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets a value indicating whether this entity has been soft-deleted.</summary>
    public bool IsDeleted { get; set; } = false;
}
