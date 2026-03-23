using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="Customer"/> with paged search support.</summary>
public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>Returns a paginated, filtered list of customers (tenant filter auto-applied).</summary>
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        string? search,
        CustomerStatus? status,
        CustomerSegment? segment,
        Guid? assignedUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a customer by its legacy/external ID, bypassing tenant filters.
    /// Used by sync operations that run without a user context.
    /// Returns null if not found.
    /// </summary>
    Task<Customer?> GetByLegacyIdAsync(
        string legacyId,
        CancellationToken cancellationToken = default);
}
