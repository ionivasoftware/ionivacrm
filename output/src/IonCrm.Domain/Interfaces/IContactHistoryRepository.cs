using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="ContactHistory"/>.</summary>
public interface IContactHistoryRepository : IRepository<ContactHistory>
{
    /// <summary>Gets all contact history records for a specific customer.</summary>
    Task<IReadOnlyList<ContactHistory>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of contact history records for a customer with optional filtering.</summary>
    Task<(IReadOnlyList<ContactHistory> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        ContactType? type,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
