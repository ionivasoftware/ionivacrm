namespace IonCrm.Application.Features.VendorInvoices.CostProviders;

/// <summary>A finalised invoice fetched from a vendor API (the actual "received" side).</summary>
public record ReceivedInvoice(int Year, int Month, decimal Amount, string Currency, string? InvoiceNumber, string? PdfUrl);

/// <summary>
/// A vendor whose finalised invoices can be pulled from an API (rather than parsed from e-mail).
/// The auto-expect run feeds these into <see cref="IVendorInvoiceService.MarkReceivedAsync"/>, filling
/// the received side with the real amount, invoice number and a viewable PDF link. Railway implements this.
/// </summary>
public interface IReceivedInvoiceSource
{
    /// <summary>Provider key (matches VendorInvoice.Provider).</summary>
    string ProviderKey { get; }

    /// <summary>True when the credentials this source needs are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Returns all finalised invoices available from the vendor API.</summary>
    Task<IReadOnlyList<ReceivedInvoice>> GetReceivedInvoicesAsync(CancellationToken cancellationToken = default);
}
