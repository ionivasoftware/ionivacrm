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
        Guid? projectId,
        string? search,
        CustomerStatus? status,
        string? segment,
        CustomerLabel? label,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(c => c.AssignedUser)
            .AsNoTracking()
            .AsQueryable();

        // Project filter (global query filter already restricts to user's projects;
        // this adds an extra explicit filter when a specific project is requested)
        if (projectId.HasValue)
            query = query.Where(c => c.ProjectId == projectId.Value);

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

        if (!string.IsNullOrWhiteSpace(segment))
            query = query.Where(c => c.Segment == segment);

        if (label.HasValue)
            query = query.Where(c => c.Label == label.Value);

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
    public async Task<Customer?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(c => c.AssignedUser)
            .Include(c => c.ContactHistories.Where(h => !h.IsDeleted))
                .ThenInclude(h => h.CreatedByUser)
            .Include(c => c.Tasks.Where(t => !t.IsDeleted))
                .ThenInclude(t => t.AssignedUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Customer?> GetByLegacyIdAsync(
        string legacyId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.LegacyId == legacyId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetParasutContactIdAsync(
        Guid customerId, string? parasutContactId,
        CancellationToken cancellationToken = default)
    {
        await Context.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Customers"" SET ""ParasutContactId"" = {0}, ""UpdatedAt"" = {1} WHERE ""Id"" = {2}",
            (object?)parasutContactId ?? DBNull.Value, DateTime.UtcNow, customerId);
    }

    /// <inheritdoc />
    public async Task TransferLeadAsync(
        Guid leadId,
        Guid targetCustomerId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;

            // Reassign all contact histories to the target customer
            await Context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""ContactHistories""
                  SET ""CustomerId"" = {0}, ""UpdatedAt"" = {1}
                  WHERE ""CustomerId"" = {2} AND ""IsDeleted"" = false",
                targetCustomerId, now, leadId);

            // Reassign all tasks to the target customer
            await Context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""CustomerTasks""
                  SET ""CustomerId"" = {0}, ""UpdatedAt"" = {1}
                  WHERE ""CustomerId"" = {2} AND ""IsDeleted"" = false",
                targetCustomerId, now, leadId);

            // Reassign all opportunities to the target customer
            await Context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Opportunities""
                  SET ""CustomerId"" = {0}, ""UpdatedAt"" = {1}
                  WHERE ""CustomerId"" = {2} AND ""IsDeleted"" = false",
                targetCustomerId, now, leadId);

            // Soft-delete the lead customer
            await Context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Customers""
                  SET ""IsDeleted"" = true, ""UpdatedAt"" = {0}
                  WHERE ""Id"" = {1}",
                now, leadId);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
