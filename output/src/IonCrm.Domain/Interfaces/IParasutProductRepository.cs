using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ParasutProduct"/> entities.
/// PROJECT-INDEPENDENT (global): a single global catalog shared by all projects, mirroring
/// the global Paraşüt connection. ProductName is the unique key.
/// </summary>
public interface IParasutProductRepository
{
    /// <summary>Returns the entire global Paraşüt product catalog (all rows).</summary>
    Task<List<ParasutProduct>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a specific Paraşüt product by primary key.</summary>
    Task<ParasutProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the global product mapping for the given product name (e.g.
    /// "RezervAl Aylık Lisans Bedeli", "1 Aylık Üyelik"). Returns null when no mapping exists.
    /// </summary>
    Task<ParasutProduct?> GetByNameAsync(string productName, CancellationToken cancellationToken = default);

    /// <summary>Persists a new Paraşüt product.</summary>
    Task<ParasutProduct> AddAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing Paraşüt product.</summary>
    Task UpdateAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>Removes a Paraşüt product (soft-delete).</summary>
    Task DeleteAsync(ParasutProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the global product whose <see cref="ParasutProduct.EmsProductId"/> matches
    /// the given EMS product ID. Used during EMS payment sync to locate the correct
    /// invoice line template.
    /// </summary>
    Task<ParasutProduct?> GetByEmsProductIdAsync(
        string emsProductId,
        CancellationToken cancellationToken = default);
}
