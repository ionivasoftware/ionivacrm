using IonCrm.Application.Common.Models;

namespace IonCrm.Application.Features.VendorInvoices.EmailCollector;

/// <summary>One matched (or attempted) invoice e-mail.</summary>
/// <param name="Provider">Matched vendor key.</param>
/// <param name="Year">Derived billing-period year.</param>
/// <param name="Month">Derived billing-period month.</param>
/// <param name="Amount">Extracted amount, when parsed.</param>
/// <param name="Currency">Currency.</param>
/// <param name="InvoiceNumber">Extracted invoice number, when found.</param>
/// <param name="Subject">E-mail subject (for the preview).</param>
/// <param name="EmailDate">E-mail date.</param>
/// <param name="Status">"received" (written), "preview" (dry-run match), "no-amount", or "failed".</param>
/// <param name="Message">Detail for failures / no-amount.</param>
public record EmailCollectItem(
    string Provider, int Year, int Month, decimal? Amount, string? Currency,
    string? InvoiceNumber, string Subject, DateTime EmailDate, string Status, string? Message);

/// <summary>Summary of an e-mail collection run.</summary>
public record EmailCollectSummary(int Scanned, int Matched, int Received, IReadOnlyList<EmailCollectItem> Items);

/// <summary>
/// Phase 3 — scans the accounting mailbox (IMAP) for vendor invoice e-mails and, per matching rule,
/// records the received figures via <see cref="IVendorInvoiceService.MarkReceivedAsync"/>.
/// </summary>
public interface IInvoiceEmailCollector
{
    /// <summary>True when IMAP host/username/password are all configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Scans recent messages and matches them to vendor rules. When <paramref name="dryRun"/> is true,
    /// returns what would be recorded without writing; otherwise calls MarkReceived for each match.
    /// </summary>
    Task<Result<EmailCollectSummary>> CollectAsync(bool dryRun, CancellationToken cancellationToken = default);
}
