using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Orchestrates the one-time data migration from the legacy MSSQL CRM database
/// (crm.bak / EMS + IONCRM databases) into the new ION CRM PostgreSQL schema.
///
/// Registered as a Singleton so migration state persists across HTTP requests.
/// Uses fire-and-forget background execution; callers poll
/// <see cref="GetStatus"/> for progress updates.
/// </summary>
public interface IMigrationService
{
    /// <summary>
    /// Starts the migration job as a background task and returns immediately.
    /// Subsequent calls while a job is running are rejected (returns false).
    /// Safe to call multiple times — idempotent once completed (re-running
    /// will skip all previously migrated records).
    /// </summary>
    /// <param name="projectId">
    /// The target project ID that all migrated customers and contact histories
    /// will be assigned to (set by SuperAdmin at trigger time).
    /// </param>
    /// <param name="mssqlConnectionString">
    /// Connection string for the legacy MSSQL database. NEVER logged.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if migration was successfully started;
    /// <see langword="false"/> if a migration is already running.
    /// </returns>
    Task<bool> StartAsync(
        Guid projectId,
        string mssqlConnectionString,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a snapshot of the current migration job status and counters.
    /// Thread-safe; can be called at any time.
    /// </summary>
    MigrationStatusDto GetStatus();
}
