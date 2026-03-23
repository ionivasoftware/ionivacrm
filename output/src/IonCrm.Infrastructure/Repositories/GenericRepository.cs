using IonCrm.Domain.Common;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// Generic EF Core repository implementation.
/// Global query filters (soft-delete + tenant isolation) are applied automatically by the DbContext.
/// </summary>
/// <typeparam name="T">Domain entity type inheriting <see cref="BaseEntity"/>.</typeparam>
public class GenericRepository<T> : IRepository<T> where T : BaseEntity
{
    /// <summary>The underlying DbContext.</summary>
    protected readonly ApplicationDbContext Context;

    /// <summary>The DbSet for entity type T.</summary>
    protected readonly DbSet<T> DbSet;

    /// <summary>Initialises a new instance of <see cref="GenericRepository{T}"/>.</summary>
    public GenericRepository(ApplicationDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    /// <inheritdoc />
    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbSet.FindAsync(new object[] { id }, cancellationToken);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        await Context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.IsDeleted = true;
        DbSet.Update(entity);
        await Context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.AnyAsync(predicate, cancellationToken);
}
