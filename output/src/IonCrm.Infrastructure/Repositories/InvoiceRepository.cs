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
            .Include(i => i.Project)
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
            .Include(i => i.Project)
            .Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> GetAllAsync(
        List<Guid>? projectIds,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters() bypasses the EF global tenant (ProjectId) filter
        // so we can safely query across multiple projects.
        var query = DbSet
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Include(i => i.Project)
            .Where(i => !i.IsDeleted); // preserve soft-delete filter manually

        if (projectIds is not null)
            query = query.Where(i => projectIds.Contains(i.ProjectId));

        return await query
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
