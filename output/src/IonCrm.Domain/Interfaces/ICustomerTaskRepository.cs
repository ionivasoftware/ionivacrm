using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="CustomerTask"/>.</summary>
public interface ICustomerTaskRepository : IRepository<CustomerTask>
{
    /// <summary>Gets all tasks for a specific customer.</summary>
    Task<IReadOnlyList<CustomerTask>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets all tasks assigned to a specific user.</summary>
    Task<IReadOnlyList<CustomerTask>> GetByAssignedUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of tasks for a customer with optional status/priority filtering.</summary>
    Task<(IReadOnlyList<CustomerTask> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        IonCrm.Domain.Enums.TaskStatus? status,
        TaskPriority? priority,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of tasks for a project with optional filters.</summary>
    Task<(IReadOnlyList<CustomerTask> Items, int TotalCount)> GetPagedByProjectAsync(
        Guid projectId,
        IonCrm.Domain.Enums.TaskStatus? status,
        TaskPriority? priority,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
