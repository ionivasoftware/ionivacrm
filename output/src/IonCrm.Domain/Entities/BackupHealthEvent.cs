using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// One DURUM DEĞİŞİKLİĞİ in Liftdesk backup health — written only when the state actually flips
/// (healthy ↔ unhealthy), never on every poll. So the table reads as a timeline: "bozuldu … düzeldi".
///
/// Why this exists at all: the dashboard card only queries while somebody has the screen open, and
/// the failure mode worth catching is precisely the one nobody is looking at. A background monitor
/// writes here so the outage has a durable start time ("3 gündür sorunlu") that survives restarts
/// and cannot be missed by simply not visiting the dashboard.
///
/// NOT tenant-scoped: a single infrastructure-wide backup covers the whole Liftdesk installation,
/// so there is no ProjectId and no tenant query filter.
/// </summary>
public class BackupHealthEvent : BaseEntity
{
    /// <summary>The state the system moved INTO at <see cref="DetectedAt"/>.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Newline-joined problems[] as reported by Liftdesk (already Turkish, operator-facing).
    /// Null/empty when the transition was a recovery.
    /// </summary>
    public string? Problems { get; set; }

    /// <summary>Hours since the last successful backup at detection time — null when unknown.</summary>
    public double? HoursSinceLastSuccessfulBackup { get; set; }

    /// <summary>When the monitor observed the change (UTC).</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
