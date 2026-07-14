using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// The stored PDF file for a <see cref="VendorInvoice"/> (one per invoice). Kept in its own table so
/// the invoice list query never loads the binary content. Populated by the e-mail collector from a PDF
/// attachment (e.g. Google Workspace invoices).
/// </summary>
public class VendorInvoicePdf : BaseEntity
{
    /// <summary>The invoice this PDF belongs to.</summary>
    public Guid VendorInvoiceId { get; set; }

    /// <summary>Original file name, when known.</summary>
    public string? FileName { get; set; }

    /// <summary>MIME type (typically application/pdf).</summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>The raw file bytes.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
