using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository interface for CRM <see cref="Invoice"/> entities.
/// </summary>
public interface IInvoiceRepository : IRepository<Invoice>
{
    /// <summary>Returns all non-deleted invoices for a given project, newest first.</summary>
    Task<IReadOnlyList<Invoice>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all non-deleted invoices for a given customer, newest first.</summary>
    Task<IReadOnlyList<Invoice>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
