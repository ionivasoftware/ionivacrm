using IonCrm.Domain.Entities;
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
}
