using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="SyncLog"/> persistence.
/// Background jobs call this directly bypassing tenant filters.
/// </summary>
public interface ISyncLogRepository
{
    /// <summary>Persists a new sync log entry and returns it with its generated Id.</summary>
    Task<SyncLog> AddAsync(SyncLog syncLog, CancellationToken cancellationToken = default);

    /// <summary>Persists all changes made to a tracked sync log (retry count, status, etc.).</summary>
    Task UpdateAsync(SyncLog syncLog, CancellationToken cancellationToken = default);

    /// <summary>Returns a paged list of sync logs. SuperAdmin sees all; others are scoped to their projects.</summary>
    Task<(List<SyncLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? projectId = null,
        SyncSource? source = null,
        SyncDirection? direction = null,
        SyncStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single sync log by its Id, or null if not found.</summary>
    Task<SyncLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
