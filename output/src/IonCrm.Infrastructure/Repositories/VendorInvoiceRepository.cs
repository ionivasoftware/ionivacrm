using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for <see cref="VendorInvoice"/>.
/// All queries use <c>IgnoreQueryFilters()</c>: the entity is global (no tenant filter) and this also
/// lets the background reconcile service run without an HTTP context. Soft-delete is applied explicitly.
/// </summary>
public sealed class VendorInvoiceRepository : IVendorInvoiceRepository
{
    private readonly ApplicationDbContext _context;

    /// <summary>Initialises a new instance of <see cref="VendorInvoiceRepository"/>.</summary>
    public VendorInvoiceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<VendorInvoice?> GetByPeriodAsync(string provider, int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.VendorInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                v => !v.IsDeleted && v.Provider == provider && v.PeriodYear == year && v.PeriodMonth == month,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<VendorInvoice>> ListAsync(
        int? year = null, int? month = null, VendorInvoiceStatus? status = null, string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.VendorInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => !v.IsDeleted);

        if (year.HasValue)   query = query.Where(v => v.PeriodYear == year.Value);
        if (month.HasValue)  query = query.Where(v => v.PeriodMonth == month.Value);
        if (status.HasValue) query = query.Where(v => v.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(v => v.Provider == provider);

        return await query
            .OrderByDescending(v => v.PeriodYear)
            .ThenByDescending(v => v.PeriodMonth)
            .ThenBy(v => v.Provider)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<VendorInvoice>> GetExpectedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.VendorInvoices
            .IgnoreQueryFilters()
            .Where(v => !v.IsDeleted && v.Status == VendorInvoiceStatus.Expected)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountMissingAsync(CancellationToken cancellationToken = default)
    {
        return await _context.VendorInvoices
            .IgnoreQueryFilters()
            .CountAsync(v => !v.IsDeleted && v.Status == VendorInvoiceStatus.Missing, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VendorInvoice> AddAsync(VendorInvoice invoice, CancellationToken cancellationToken = default)
    {
        if (invoice.Id == Guid.Empty) invoice.Id = Guid.NewGuid();
        await _context.VendorInvoices.AddAsync(invoice, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(VendorInvoice invoice, CancellationToken cancellationToken = default)
    {
        _context.VendorInvoices.Update(invoice);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
