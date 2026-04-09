using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for <see cref="ParasutProduct"/>.
/// All reads ignore query filters: the entity has no tenant filter (it's project-independent)
/// but we keep IgnoreQueryFilters() so background jobs without HTTP context behave the same
/// as user-driven requests, and so soft-delete is the only filter applied.
/// </summary>
public class ParasutProductRepository : IParasutProductRepository
{
    private readonly ApplicationDbContext _context;

    /// <summary>Initialises a new instance of <see cref="ParasutProductRepository"/>.</summary>
    public ParasutProductRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<ParasutProduct>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutProducts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.ProductName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutProduct?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutProducts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutProduct?> GetByNameAsync(
        string productName,
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutProducts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.ProductName == productName && !p.IsDeleted,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutProduct> AddAsync(
        ParasutProduct product,
        CancellationToken cancellationToken = default)
    {
        product.Id = Guid.NewGuid();
        await _context.ParasutProducts.AddAsync(product, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return product;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        ParasutProduct product,
        CancellationToken cancellationToken = default)
    {
        product.UpdatedAt = DateTime.UtcNow;
        _context.ParasutProducts.Update(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        ParasutProduct product,
        CancellationToken cancellationToken = default)
    {
        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;
        _context.ParasutProducts.Update(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutProduct?> GetByEmsProductIdAsync(
        string emsProductId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ParasutProducts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.EmsProductId == emsProductId && !p.IsDeleted,
                cancellationToken);
    }
}
