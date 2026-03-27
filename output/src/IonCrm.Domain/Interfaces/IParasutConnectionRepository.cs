using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ParasutConnection"/> entities.
/// Each project has at most one Paraşüt connection.
/// </summary>
public interface IParasutConnectionRepository
{
    /// <summary>Returns the Paraşüt connection for the given project, or null if none exists.</summary>
    Task<ParasutConnection?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new Paraşüt connection.</summary>
    Task<ParasutConnection> AddAsync(ParasutConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing Paraşüt connection (e.g., refreshed tokens).</summary>
    Task UpdateAsync(ParasutConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Removes the Paraşüt connection for the given project (soft-delete).</summary>
    Task DeleteAsync(ParasutConnection connection, CancellationToken cancellationToken = default);
}
