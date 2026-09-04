using System.Text.Json;

namespace IonCrm.Application.Common.Models.ExternalApis;

/// <summary>
/// One backup-pipeline run reported by Liftdesk (docs/crm-backup-api.md §5).
///
/// NOT tenant-scoped: there is a single infrastructure-wide backup covering the whole Liftdesk
/// installation, so this must never be surfaced on a customer card as "that firm's backup".
/// All timestamps are UTC and must be converted for display.
/// </summary>
public record LiftdeskBackupRun(
    Guid Id,
    /// <summary>Backup | Verify | Mirror.</summary>
    string Kind,
    /// <summary>Running | Succeeded | Failed.</summary>
    string Status,
    string? BackupName,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationSeconds,
    /// <summary>Encrypted archive size in bytes (~1.2 GB). Convert with 1024³.</summary>
    long? SizeBytes,
    /// <summary>Source database size in bytes (~8.6 GB) — growth tracking.</summary>
    long? DatabaseSizeBytes,
    int? ArchiveEntries,
    string? Destination,
    /// <summary>
    /// Row counts captured at dump time. Deliberately kept as raw JSON: the contract states the
    /// field set is NOT fixed, so unknown keys must survive and missing ones must not throw.
    /// </summary>
    JsonElement? SourceCounts,
    /// <summary>full (data included) | schema (schema only — the weak state).</summary>
    string? VerifyMode,
    /// <summary>Whether restored row counts matched the dump manifest.</summary>
    bool? CountsMatched,
    string? Message,
    /// <summary>GitHub Actions run link — the "Logu aç" button target.</summary>
    string? RunUrl,
    string? TriggeredBy);

/// <summary>
/// Dashboard-card payload from GET /api/v1/crm/backups/status (docs/crm-backup-api.md §3).
///
/// <see cref="IsHealthy"/> is the single field an operator reads; when false,
/// <see cref="Problems"/> carries operator-facing Turkish reasons that can be rendered verbatim.
///
/// IMPORTANT: silence is not success. No records at all yields IsHealthy=false — the CRM must not
/// treat an empty payload as "nothing to show"; that is precisely the case worth catching.
/// </summary>
public record LiftdeskBackupStatus(
    bool IsHealthy,
    List<string>? Problems,
    LiftdeskBackupRun? LastBackup,
    LiftdeskBackupRun? LastSuccessfulBackup,
    double? HoursSinceLastSuccessfulBackup,
    LiftdeskBackupRun? LastVerify,
    LiftdeskBackupRun? LastSuccessfulVerify,
    double? HoursSinceLastSuccessfulVerify,
    LiftdeskBackupRun? LastMirror,
    int FailuresLast7Days,
    long? LatestBackupSizeBytes,
    long? LatestDatabaseSizeBytes);
