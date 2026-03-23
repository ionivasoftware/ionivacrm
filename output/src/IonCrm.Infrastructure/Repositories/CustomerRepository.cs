using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for <see cref="Customer"/> with search and pagination support.
/// Tenant filtering is handled by the global query filter in <see cref="ApplicationDbContext"/>.
/// </summary>
public class CustomerRepository : GenericRepository<Customer>, ICustomerRepository
{
    /// <summary>Initialises a new instance of <see cref="CustomerRepository"/>.</summary>
    public CustomerRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        string? search,
        CustomerStatus? status,
        CustomerSegment? segment,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(c => c.AssignedUser)
            .AsNoTracking()
            .AsQueryable();

        // Full-text search across key fields
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.CompanyName.ToLower().Contains(term) ||
                (c.ContactName != null && c.ContactName.ToLower().Contains(term)) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.Code != null && c.Code.ToLower().Contains(term)));
        }

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        if (segment.HasValue)
            query = query.Where(c => c.Segment == segment.Value);

        if (assignedUserId.HasValue)
            query = query.Where(c => c.AssignedUserId == assignedUserId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.CompanyName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<Customer?> GetByLegacyIdAsync(
        string legacyId,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters bypasses the tenant + soft-delete global filter.
        // We explicitly filter IsDeleted = false for safety.
        return await DbSet
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
