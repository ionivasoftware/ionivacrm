using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a CRM invoice that can optionally be transferred to Paraşüt.
/// Two-step flow: 1) saved in CRM with ParasutId=null, 2) manually transferred → ParasutId set.
/// </summary>
public class Invoice : BaseEntity
{
    /// <summary>Gets or sets the tenant (project) this invoice belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the customer this invoice is for.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the invoice title / short description.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the detailed description shown on the invoice.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the invoice series identifier (e.g. "A", "INV").</summary>
    public string? InvoiceSeries { get; set; }

    /// <summary>Gets or sets the invoice sequence number within the series.</summary>
    public int? InvoiceNumber { get; set; }

    /// <summary>Gets or sets the invoice issue date (UTC).</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>Gets or sets the payment due date (UTC).</summary>
    public DateTime DueDate { get; set; }

    /// <summary>Gets or sets the currency code ("TRL", "USD", "EUR", "GBP").</summary>
    public string Currency { get; set; } = "TRL";

    /// <summary>Gets or sets the gross total (including VAT).</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>Gets or sets the net total (excluding VAT).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>
    /// Gets or sets the invoice line items serialized as JSON.
    /// Format: array of { description, quantity, unitPrice, vatRate, discountValue, discountType, unit }.
    /// </summary>
    public string LinesJson { get; set; } = "[]";

    /// <summary>Gets or sets the invoice lifecycle status.</summary>
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>
    /// Gets or sets the Paraşüt sales invoice ID.
    /// Null until the invoice is transferred to Paraşüt.
    /// </summary>
    public string? ParasutId { get; set; }

    /// <summary>
    /// Gets or sets the EMS payment ID that triggered auto-creation of this invoice draft.
    /// Used to prevent duplicate invoice drafts when the sync job runs multiple times
    /// within the same payment window.
    /// Null for invoices created manually.
    /// </summary>
    public string? EmsPaymentId { get; set; }

    // ── Navigation properties ─────────────────────────────────────────────────

    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the customer this invoice belongs to.</summary>
    public Customer Customer { get; set; } = null!;
}
