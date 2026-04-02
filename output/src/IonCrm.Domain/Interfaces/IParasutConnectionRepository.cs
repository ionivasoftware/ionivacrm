using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ParasutConnection"/> entities.
///
/// Connection lookup strategy:
///   <list type="bullet">
///     <item><description>Project-specific — <c>ProjectId == projectId</c></description></item>
///     <item><description>Global          — <c>ProjectId == null</c> (shared by all projects)</description></item>
///   </list>
///
/// Use <see cref="GetEffectiveConnectionAsync"/> in service/query handlers to get the
/// best-available connection (project-specific with automatic fallback to global).
/// Use <see cref="GetByProjectIdAsync"/> and <see cref="GetGlobalAsync"/> directly only
/// when an exact lookup is needed (e.g., during connect/disconnect upsert logic).
/// </summary>
public interface IParasutConnectionRepository
{
    /// <summary>
    /// Returns the strictly project-specific Paraşüt connection, or null if none exists.
    /// Does NOT fall back to the global connection.
    /// Use this for connect/disconnect upsert checks.
    /// </summary>
    Task<ParasutConnection?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the effective Paraşüt connection for the given project:
    /// project-specific first, falling back to the global connection (<c>ProjectId == null</c>).
    /// Use this in service/query handlers.
    /// </summary>
    Task<ParasutConnection?> GetEffectiveConnectionAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the global Paraşüt connection (<c>ProjectId == null</c>), or null if none exists.
    /// </summary>
    Task<ParasutConnection?> GetGlobalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ALL Paraşüt connections across all tenants (including the global one),
    /// bypassing query filters. Used by background/startup services that have no HTTP context.
    /// </summary>
    Task<List<ParasutConnection>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a new Paraşüt connection.</summary>
    Task<ParasutConnection> AddAsync(ParasutConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing Paraşüt connection (e.g., refreshed tokens).</summary>
    Task UpdateAsync(ParasutConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the given Paraşüt connection.</summary>
    Task DeleteAsync(ParasutConnection connection, CancellationToken cancellationToken = default);
}
