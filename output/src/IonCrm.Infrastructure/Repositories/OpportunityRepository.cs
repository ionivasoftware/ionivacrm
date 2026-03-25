using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core repository for <see cref="Opportunity"/>.</summary>
public class OpportunityRepository : GenericRepository<Opportunity>, IOpportunityRepository
{
    public OpportunityRepository(ApplicationDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Opportunity> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(o => o.Customer)
            .Include(o => o.AssignedUser)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
