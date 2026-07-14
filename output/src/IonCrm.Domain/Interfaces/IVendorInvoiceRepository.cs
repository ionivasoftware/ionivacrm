using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="VendorInvoice"/> reconciliation records.
/// Global (no tenant filter) — SuperAdmin-only operational cost tracking.
/// </summary>
public interface IVendorInvoiceRepository
{
    /// <summary>Finds the record for a specific (provider, year, month), or null.</summary>
    Task<VendorInvoice?> GetByPeriodAsync(string provider, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists records filtered by any combination of year, month, status and provider.
    /// Ordered by period (newest first) then provider.
    /// </summary>
    Task<List<VendorInvoice>> ListAsync(
        int? year = null,
        int? month = null,
        VendorInvoiceStatus? status = null,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all records still in <see cref="VendorInvoiceStatus.Expected"/> (for the reconcile sweep).</summary>
    Task<List<VendorInvoice>> GetExpectedAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts records currently in <see cref="VendorInvoiceStatus.Missing"/> (for the alarm badge).</summary>
    Task<int> CountMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new record.</summary>
    Task<VendorInvoice> AddAsync(VendorInvoice invoice, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to a tracked record.</summary>
    Task UpdateAsync(VendorInvoice invoice, CancellationToken cancellationToken = default);
}
