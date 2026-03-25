using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
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

    /// <inheritdoc />
    public async Task<(IReadOnlyList<CustomerTask> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        IonCrm.Domain.Enums.TaskStatus? status,
        TaskPriority? priority,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(t => t.AssignedUser)
            .Where(t => t.CustomerId == customerId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<CustomerTask> Items, int TotalCount)> GetPagedByProjectAsync(
        Guid projectId,
        IonCrm.Domain.Enums.TaskStatus? status,
        TaskPriority? priority,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(t => t.AssignedUser)
            .Include(t => t.Customer)
            .Where(t => t.ProjectId == projectId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        if (assignedUserId.HasValue)
            query = query.Where(t => t.AssignedUserId == assignedUserId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
