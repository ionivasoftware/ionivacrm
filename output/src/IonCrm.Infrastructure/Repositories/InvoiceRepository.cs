using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for <see cref="Invoice"/> entities.
/// Global query filters (soft-delete + tenant) are applied automatically by <see cref="ApplicationDbContext"/>.
/// </summary>
public class InvoiceRepository : GenericRepository<Invoice>, IInvoiceRepository
{
    /// <summary>Initialises a new instance of <see cref="InvoiceRepository"/>.</summary>
    public InvoiceRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
        => await DbSet
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.ProjectId == projectId)
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await DbSet
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
}
