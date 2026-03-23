using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Audit log of all sync operations between ION CRM and external SaaS systems.
/// Failed syncs are retried up to 3 times with exponential backoff.
/// </summary>
public class SyncLog : BaseEntity
{
    /// <summary>Gets or sets the project (tenant) this sync belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the external system involved in this sync.</summary>
    public SyncSource Source { get; set; }

    /// <summary>Gets or sets the direction of data flow.</summary>
    public SyncDirection Direction { get; set; }

    /// <summary>Gets or sets the name of the entity type being synced (e.g., "Customer").</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifier of the entity being synced.</summary>
    public string? EntityId { get; set; }

    /// <summary>Gets or sets the current status of this sync operation.</summary>
    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    /// <summary>Gets or sets the error message if the sync failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the number of retry attempts made.</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>Gets or sets the UTC timestamp when the sync completed (null if pending).</summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>Gets or sets the raw payload (JSON) stored for debugging and retry purposes.</summary>
    public string? Payload { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;
}
