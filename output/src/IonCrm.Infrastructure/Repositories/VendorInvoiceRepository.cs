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

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.VendorInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);
        if (entity is null) return false;

        entity.IsDeleted = true;
        _context.VendorInvoices.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ── PDF storage ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task SavePdfAsync(Guid vendorInvoiceId, string? fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        var existing = await _context.VendorInvoicePdfs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.VendorInvoiceId == vendorInvoiceId && !p.IsDeleted, cancellationToken);

        if (existing is null)
        {
            await _context.VendorInvoicePdfs.AddAsync(new VendorInvoicePdf
            {
                Id = Guid.NewGuid(),
                VendorInvoiceId = vendorInvoiceId,
                FileName = fileName,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
                Content = content,
            }, cancellationToken);
        }
        else
        {
            existing.FileName = fileName;
            existing.ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType;
            existing.Content = content;
            _context.VendorInvoicePdfs.Update(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VendorInvoicePdf?> GetPdfAsync(Guid vendorInvoiceId, CancellationToken cancellationToken = default)
    {
        return await _context.VendorInvoicePdfs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.VendorInvoiceId == vendorInvoiceId && !p.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HashSet<Guid>> GetInvoiceIdsWithPdfAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _context.VendorInvoicePdfs
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .Select(p => p.VendorInvoiceId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}
