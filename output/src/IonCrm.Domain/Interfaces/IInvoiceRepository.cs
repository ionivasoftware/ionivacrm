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

    /// <summary>
    /// Returns invoices across multiple projects, bypassing the EF global tenant filter.
    /// When <paramref name="projectIds"/> is null, returns all invoices (SuperAdmin use).
    /// When provided, returns only invoices whose ProjectId is in the list.
    /// Results are ordered newest IssueDate first.
    /// </summary>
    Task<IReadOnlyList<Invoice>> GetAllAsync(
        List<Guid>? projectIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if an invoice draft already exists for the given EMS payment ID.
    /// Used by the EMS payment sync job to prevent duplicate invoice drafts.
    /// Bypasses global query filters so it works across tenants in background jobs.
    /// </summary>
    Task<bool> ExistsByEmsPaymentIdAsync(
        string emsPaymentId,
        CancellationToken cancellationToken = default);
}
