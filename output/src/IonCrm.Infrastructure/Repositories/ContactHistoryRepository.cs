using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core repository for <see cref="ContactHistory"/>.</summary>
public class ContactHistoryRepository : GenericRepository<ContactHistory>, IContactHistoryRepository
{
    /// <summary>Initialises a new instance of <see cref="ContactHistoryRepository"/>.</summary>
    public ContactHistoryRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContactHistory>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await DbSet
            .Include(h => h.CreatedByUser)
            .Where(h => h.CustomerId == customerId)
            .OrderByDescending(h => h.ContactedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ContactHistory> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        ContactType? type,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(h => h.CreatedByUser)
            .Where(h => h.CustomerId == customerId);

        if (type.HasValue)
            query = query.Where(h => h.Type == type.Value);

        if (fromDate.HasValue)
            query = query.Where(h => h.ContactedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(h => h.ContactedAt <= toDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(h => h.ContactedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ContactHistory> Items, int TotalCount)> GetPagedAllAsync(
        Guid? projectId,
        Guid? customerId,
        ContactType? type,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Global query filter already restricts to user's accessible projects (tenant isolation).
        var query = DbSet
            .Include(h => h.CreatedByUser)
            .Include(h => h.Customer)
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(h => h.ProjectId == projectId.Value);

        if (customerId.HasValue)
            query = query.Where(h => h.CustomerId == customerId.Value);

        if (type.HasValue)
            query = query.Where(h => h.Type == type.Value);

        if (fromDate.HasValue)
            query = query.Where(h => h.ContactedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(h => h.ContactedAt <= toDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(h => h.ContactedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
