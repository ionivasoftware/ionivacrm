using IonCrm.Domain.Entities;

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
}
