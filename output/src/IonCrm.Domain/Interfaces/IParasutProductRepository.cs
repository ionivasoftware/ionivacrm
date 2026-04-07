using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ParasutProduct"/> entities.
/// Each project maintains a catalog of 6 configurable products for invoice line items.
/// </summary>
public interface IParasutProductRepository
{
    /// <summary>Returns all Paraşüt products for the given project.</summary>
    Task<List<ParasutProduct>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Returns a specific Paraşüt product by ID.</summary>
    Task<ParasutProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a product by name within the given project.
    /// Useful for looking up products during invoice creation.
    /// </summary>
    Task<ParasutProduct?> GetByNameAsync(Guid projectId, string productName, CancellationToken cancellationToken = default);

    /// <summary>Persists a new Paraşüt product.</summary>
    Task<ParasutProduct> AddAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing Paraşüt product.</summary>
    Task UpdateAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>Removes a Paraşüt product (soft-delete).</summary>
    Task DeleteAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the product whose <see cref="ParasutProduct.EmsProductId"/> matches
    /// the given EMS product ID within the project.
    /// Used during EMS payment sync to locate the correct invoice line template.
    /// </summary>
    Task<ParasutProduct?> GetByEmsProductIdAsync(
        Guid projectId,
        string emsProductId,
        CancellationToken cancellationToken = default);
}
