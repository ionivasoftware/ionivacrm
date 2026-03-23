using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core repository for <see cref="CustomerTask"/>.</summary>
public class CustomerTaskRepository : GenericRepository<CustomerTask>, ICustomerTaskRepository
{
    /// <summary>Initialises a new instance of <see cref="CustomerTaskRepository"/>.</summary>
    public CustomerTaskRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerTask>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await DbSet
            .Include(t => t.AssignedUser)
            .Where(t => t.CustomerId == customerId)
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerTask>> GetByAssignedUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await DbSet
            .Include(t => t.Customer)
            .Where(t => t.AssignedUserId == userId)
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}
