using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.VendorInvoices;

// ── Operation inputs ──────────────────────────────────────────────────────────

/// <summary>Idempotent upsert of an expected bill. Refreshes the amount without downgrading the status.</summary>
public record ExpectRequest(
    string Provider,
    int Year,
    int Month,
    decimal? ExpectedAmount = null,
    string? Currency = null,
    int? DueDay = null,
    VendorBillingType? BillingType = null);

/// <summary>Records that the PDF invoice arrived. Creates the record if it doesn't exist.</summary>
public record MarkReceivedRequest(
    string Provider,
    int Year,
    int Month,
    decimal? ReceivedAmount = null,
    string? Currency = null,
    string? InvoiceNumber = null,
    string? PdfUrl = null);

/// <summary>Result of a reconcile sweep: the records that were flipped to Missing.</summary>
public record ReconcileResult(int MissingCount, IReadOnlyList<VendorInvoiceDto> Missing);

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Vendor-invoice reconciliation operations (Phase 1: reconcile + alarm skeleton).
/// State machine: Expected → Received → Reconciled | Mismatch; Expected → Missing (overdue).
/// </summary>
public interface IVendorInvoiceService
{
    /// <summary>
    /// Idempotent upsert of an expected bill. If the record exists, refreshes ExpectedAmount/Currency/DueDay
    /// but never downgrades the status (Received/Reconciled/Mismatch are preserved). New records start Expected.
    /// </summary>
    Task<Result<VendorInvoiceDto>> ExpectAsync(ExpectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the received PDF. Finds (or creates) the record, stamps ReceivedOn, clears AlertedOn,
    /// then either reconciles against ExpectedAmount (→ Reconciled/Mismatch) or leaves it Received.
    /// </summary>
    Task<Result<VendorInvoiceDto>> MarkReceivedAsync(MarkReceivedRequest request, CancellationToken cancellationToken = default);

    /// <summary>Seeds baseline Expected rows for all <see cref="KnownProviders"/> for the given period (idempotent).</summary>
    Task<Result<List<VendorInvoiceDto>>> SeedMonthAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flips every Expected record whose due date has passed (relative to <paramref name="asOf"/>, default now)
    /// to Missing and stamps AlertedOn. Returns the Missing list for alerting.
    /// </summary>
    Task<Result<ReconcileResult>> ReconcileAsync(DateTime? asOf = null, CancellationToken cancellationToken = default);

    /// <summary>Lists reconciliation records for the screen, filtered by year/month/status/provider.</summary>
    Task<Result<List<VendorInvoiceDto>>> ListAsync(
        int? year = null,
        int? month = null,
        VendorInvoiceStatus? status = null,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Number of records currently in Missing status (for the red badge).</summary>
    Task<int> CountMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a reconciliation record by id.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stores (or replaces) the PDF file for an invoice.</summary>
    Task<Result> SavePdfAsync(Guid invoiceId, string? fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored PDF for an invoice, or null when none exists.</summary>
    Task<VendorInvoicePdfResult?> GetPdfAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}

/// <summary>The bytes + metadata of a stored invoice PDF.</summary>
public record VendorInvoicePdfResult(byte[] Content, string ContentType, string? FileName);
