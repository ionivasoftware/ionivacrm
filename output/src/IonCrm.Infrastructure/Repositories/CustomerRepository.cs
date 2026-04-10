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
    public async Task<(IReadOnlyList<(Customer Customer, DateTime? LastActivityDate)> Items, int TotalCount)> GetPagedAsync(
        Guid? projectId,
        string? search,
        CustomerStatus? status,
        string? segment,
        CustomerLabel? label,
        Guid? assignedUserId,
        int page,
        int pageSize,
        string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(c => c.AssignedUser)
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(c => c.ProjectId == projectId.Value);

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

        // Project customers with their most recent ContactHistory date via a correlated
        // subquery. EF Core translates this into a single SQL query with a scalar subselect.
        var projected = query.Select(c => new
        {
            Customer = c,
            LastActivityDate = Context.ContactHistories
                .Where(h => h.CustomerId == c.Id && !h.IsDeleted)
                .Max(h => (DateTime?)h.ContactedAt)
        });

        // Sort: default is lastActivity descending (newest first).
        var sorted = sortBy switch
        {
            "name"         => projected.OrderBy(x => x.Customer.CompanyName),
            "name_desc"    => projected.OrderByDescending(x => x.Customer.CompanyName),
            "created"      => projected.OrderBy(x => x.Customer.CreatedAt),
            "created_desc" => projected.OrderByDescending(x => x.Customer.CreatedAt),
            "activity"     => projected.OrderBy(x => x.LastActivityDate),
            _              => projected.OrderByDescending(x => x.LastActivityDate)
                                       .ThenByDescending(x => x.Customer.CreatedAt), // "activity_desc" or default
        };

        var items = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = items
            .Select(x => (x.Customer, x.LastActivityDate))
            .ToList();

        return (result, totalCount);
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
    public async Task<Customer?> GetByIdIgnoringTenantAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
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
