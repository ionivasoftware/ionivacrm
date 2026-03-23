using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="ContactHistory"/>.</summary>
public interface IContactHistoryRepository : IRepository<ContactHistory>
{
    /// <summary>Gets all contact history records for a specific customer.</summary>
    Task<IReadOnlyList<ContactHistory>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
