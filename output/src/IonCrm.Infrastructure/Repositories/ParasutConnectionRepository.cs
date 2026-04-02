using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core repository for <see cref="ParasutConnection"/>.</summary>
public class ParasutConnectionRepository : IParasutConnectionRepository
{
    private readonly ApplicationDbContext _context;

    /// <summary>Initialises a new instance of <see cref="ParasutConnectionRepository"/>.</summary>
    public ParasutConnectionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    /// <remarks>Strictly project-specific — does NOT fall back to the global connection.</remarks>
    public async Task<ParasutConnection?> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && !c.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutConnection?> GetEffectiveConnectionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Project-specific takes priority; fall back to global (ProjectId == null)
        var projectConn = await GetByProjectIdAsync(projectId, cancellationToken);
        return projectConn ?? await GetGlobalAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutConnection?> GetGlobalAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == null && !c.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ParasutConnection>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters() is REQUIRED — no HTTP context means tenant filter would block all rows.
        return await _context.ParasutConnections
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutConnection> AddAsync(
        ParasutConnection connection,
        CancellationToken cancellationToken = default)
    {
        connection.Id = Guid.NewGuid();
        await _context.ParasutConnections.AddAsync(connection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return connection;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        ParasutConnection connection,
        CancellationToken cancellationToken = default)
    {
        _context.ParasutConnections.Update(connection);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        ParasutConnection connection,
        CancellationToken cancellationToken = default)
    {
        connection.IsDeleted = true;
        _context.ParasutConnections.Update(connection);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
