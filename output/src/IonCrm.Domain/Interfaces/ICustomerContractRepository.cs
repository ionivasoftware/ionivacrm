using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository interface for <see cref="CustomerContract"/> entities.
/// All read methods bypass the EF global tenant filter so they work in
/// background-job contexts where there is no HTTP user.
/// </summary>
public interface ICustomerContractRepository
{
    /// <summary>Returns the contract by its primary key, or <c>null</c> if not found.</summary>
    Task<CustomerContract?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the currently <see cref="Domain.Enums.ContractStatus.Active"/> contract for a customer,
    /// or <c>null</c> when there is no active contract.
    /// Each customer is expected to have at most one active contract at a time
    /// (renewal completes the previous active contract before creating a new one).
    /// </summary>
    Task<CustomerContract?> GetActiveByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active EFT/Wire contracts whose <see cref="CustomerContract.NextInvoiceDate"/>
    /// is on or before <paramref name="today"/> and whose <see cref="CustomerContract.EndDate"/>
    /// has not passed (or is null = indefinite). Used by the monthly invoice background job.
    /// </summary>
    Task<IReadOnlyList<CustomerContract>> GetActiveEftContractsDueAsync(
        DateTime today,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts a new contract and returns it with the generated Id populated.</summary>
    Task<CustomerContract> AddAsync(
        CustomerContract contract,
        CancellationToken cancellationToken = default);

    /// <summary>Persists changes made to an existing tracked or detached contract entity.</summary>
    Task UpdateAsync(
        CustomerContract contract,
        CancellationToken cancellationToken = default);
}
