using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="Customer"/> with paged search support.</summary>
public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>Returns a paginated, filtered list of customers (tenant filter auto-applied via global query filter).</summary>
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        Guid? projectId,
        string? search,
        CustomerStatus? status,
        string? segment,
        CustomerLabel? label,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a customer with all navigation properties loaded (contact histories, tasks, assigned user).</summary>
    Task<Customer?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a customer by its legacy/external ID, bypassing tenant filters.
    /// Used by sync operations that run without a user context.
    /// Returns null if not found.
    /// </summary>
    Task<Customer?> GetByLegacyIdAsync(
        string legacyId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates only the ParasutContactId column via targeted SQL — avoids full entity update.</summary>
    Task SetParasutContactIdAsync(Guid customerId, string? parasutContactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transfers all ContactHistories, CustomerTasks and Opportunities from the lead customer
    /// to the target customer, then soft-deletes the lead. All operations run in a single DB transaction.
    /// </summary>
    Task TransferLeadAsync(Guid leadId, Guid targetCustomerId, CancellationToken cancellationToken = default);
}
