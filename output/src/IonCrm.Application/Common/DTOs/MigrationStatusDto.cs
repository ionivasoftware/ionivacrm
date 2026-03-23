namespace IonCrm.Application.Common.DTOs;

/// <summary>Lifecycle state of the one-time data migration job.</summary>
public enum MigrationState
{
    /// <summary>No migration has been started yet (or service was restarted).</summary>
    Idle,

    /// <summary>Migration is currently running in the background.</summary>
    Running,

    /// <summary>Migration completed successfully.</summary>
    Completed,

    /// <summary>Migration failed — see <see cref="MigrationStatusDto.Errors"/>.</summary>
    Failed
}

/// <summary>
/// Real-time snapshot of the data migration job progress.
/// Returned by POST /api/v1/migration/run and GET /api/v1/migration/status.
/// </summary>
public class MigrationStatusDto
{
    /// <summary>Gets or sets the overall state of the migration job.</summary>
    public MigrationState State { get; set; } = MigrationState.Idle;

    /// <summary>Gets or sets a human-readable description of the current operation.</summary>
    public string CurrentOperation { get; set; } = "No migration has been started.";

    /// <summary>Gets or sets the target project all migrated records are assigned to.</summary>
    public Guid? TargetProjectId { get; set; }

    // ── Customer counters ─────────────────────────────────────────────────────

    /// <summary>Gets or sets the total number of customer rows found in the legacy database.</summary>
    public int TotalCustomers { get; set; }

    /// <summary>Gets or sets the number of new customer records inserted.</summary>
    public int MigratedCustomers { get; set; }

    /// <summary>Gets or sets the number of customers skipped because they already exist (idempotent).</summary>
    public int SkippedCustomers { get; set; }

    // ── Contact history counters ──────────────────────────────────────────────

    /// <summary>Gets or sets the total number of contact history rows found in the legacy database.</summary>
    public int TotalContactHistories { get; set; }

    /// <summary>Gets or sets the number of new contact history records inserted.</summary>
    public int MigratedContactHistories { get; set; }

    /// <summary>Gets or sets the number of contact histories skipped because they already exist.</summary>
    public int SkippedContactHistories { get; set; }

    // ── Progress ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the overall progress percentage (0–100).
    /// Calculated from total vs processed records across all phases.
    /// </summary>
    public int ProgressPercent
    {
        get
        {
            if (State == MigrationState.Completed) return 100;
            if (State == MigrationState.Idle) return 0;

            var total = TotalCustomers + TotalContactHistories;
            if (total == 0) return 0;

            var done = MigratedCustomers + SkippedCustomers
                     + MigratedContactHistories + SkippedContactHistories;
            return (int)Math.Min(100, done * 100.0 / total);
        }
    }

    // ── Errors ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets non-fatal row-level errors encountered during migration.
    /// Fatal errors that abort the job are also included here.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    // ── Timestamps ────────────────────────────────────────────────────────────

    /// <summary>Gets or sets when the current migration run started (UTC).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Gets or sets when the migration completed or failed (UTC).</summary>
    public DateTime? CompletedAt { get; set; }
}
