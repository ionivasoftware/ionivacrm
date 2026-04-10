using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="Customer"/> with paged search support.</summary>
public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>Returns a paginated, filtered list of customers with their last activity date.</summary>
    Task<(IReadOnlyList<(Customer Customer, DateTime? LastActivityDate)> Items, int TotalCount)> GetPagedAsync(
        Guid? projectId,
        string? search,
        CustomerStatus? status,
        string? segment,
        CustomerLabel? label,
        Guid? assignedUserId,
        int page,
        int pageSize,
        string? sortBy = null,
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

    /// <summary>
    /// Returns a customer by primary key, bypassing tenant filters and soft-delete check.
    /// Used by background jobs (no HTTP context → empty ProjectIds → tenant filter would
    /// hide every row). Returns null only if the row genuinely does not exist.
    /// </summary>
    Task<Customer?> GetByIdIgnoringTenantAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Updates only the ParasutContactId column via targeted SQL — avoids full entity update.</summary>
    Task SetParasutContactIdAsync(Guid customerId, string? parasutContactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transfers all ContactHistories, CustomerTasks and Opportunities from the lead customer
    /// to the target customer, then soft-deletes the lead. All operations run in a single DB transaction.
    /// </summary>
    Task TransferLeadAsync(Guid leadId, Guid targetCustomerId, CancellationToken cancellationToken = default);
}
