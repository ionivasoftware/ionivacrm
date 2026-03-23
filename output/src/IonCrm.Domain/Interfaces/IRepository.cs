using IonCrm.Domain.Common;
using System.Linq.Expressions;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Generic repository interface — defines the contract for all data access.
/// All implementations in IonCrm.Infrastructure.Repositories.
/// </summary>
/// <typeparam name="T">The domain entity type, must inherit from BaseEntity.</typeparam>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>Gets an entity by its primary key.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets all non-deleted entities (tenant filter applied automatically).</summary>
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds entities matching a predicate (tenant filter applied automatically).</summary>
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Adds a new entity to the repository.</summary>
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing entity.</summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an entity (sets IsDeleted = true).</summary>
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Checks whether any entity matches the given predicate.</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}
