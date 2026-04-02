using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for <see cref="SyncLog"/> with admin-level access.
/// Background jobs call this without a user context, so queries use
/// <c>IgnoreQueryFilters()</c> with explicit IsDeleted checks.
/// SuperAdmin API access respects global filters via the standard context.
/// </summary>
public sealed class SyncLogRepository : ISyncLogRepository
{
    private readonly ApplicationDbContext _context;

    /// <summary>Initialises a new instance of <see cref="SyncLogRepository"/>.</summary>
    public SyncLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<SyncLog> AddAsync(SyncLog syncLog, CancellationToken cancellationToken = default)
    {
        syncLog.CreatedAt = DateTime.UtcNow;
        syncLog.UpdatedAt = DateTime.UtcNow;

        await _context.SyncLogs.AddAsync(syncLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return syncLog;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SyncLog syncLog, CancellationToken cancellationToken = default)
    {
        syncLog.UpdatedAt = DateTime.UtcNow;
        _context.SyncLogs.Update(syncLog);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(List<SyncLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? projectId = null,
        SyncSource? source = null,
        SyncDirection? direction = null,
        SyncStatus? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        // Use IgnoreQueryFilters to support background job contexts,
        // then apply explicit filters.
        var query = _context.SyncLogs
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted)
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(s => s.ProjectId == projectId.Value);

        if (source.HasValue)
            query = query.Where(s => s.Source == source.Value);

        if (direction.HasValue)
            query = query.Where(s => s.Direction == direction.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (fromDate.HasValue)
            query = query.Where(s => s.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(s => s.CreatedAt <= toDate.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<SyncLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.SyncLogs
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
