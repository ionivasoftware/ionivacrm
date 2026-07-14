using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Reconciliation record for a single vendor bill for a single period (provider + year + month).
///
/// PROJECT-INDEPENDENT (global): these are the company's own operational costs (Anthropic, Railway,
/// Google Cloud, Google Workspace), not tenant data — so there is no ProjectId / tenant filter and
/// the screen is SuperAdmin-only.
///
/// The reconciliation idea: providers have no PDF-invoice API (PDFs only arrive by e-mail), so we
/// learn a bill *exists* from an independent signal (cost API for Usage, the fixed price for Fixed),
/// record the PDF amount when it arrives, and compare the two. If the PDF never arrives by the due
/// date, the record flips to <see cref="VendorInvoiceStatus.Missing"/> and is alarmed.
///
/// Uniqueness: (Provider, PeriodYear, PeriodMonth) — enforced by a unique index for idempotent upsert.
/// </summary>
public class VendorInvoice : BaseEntity
{
    /// <summary>Provider key, e.g. "Anthropic", "Railway", "GoogleCloud", "GoogleWorkspace". Free text (max 50) for extensibility.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Billing period year (e.g. 2026).</summary>
    public int PeriodYear { get; set; }

    /// <summary>Billing period month, 1–12.</summary>
    public int PeriodMonth { get; set; }

    /// <summary>Whether the amount varies (Usage) or is a flat subscription (Fixed).</summary>
    public VendorBillingType BillingType { get; set; } = VendorBillingType.Usage;

    /// <summary>Current reconciliation status.</summary>
    public VendorInvoiceStatus Status { get; set; } = VendorInvoiceStatus.Expected;

    /// <summary>Amount we expect to be billed, from the cost API or the fixed price. Null until known.</summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>Amount read from the received PDF invoice. Null until the PDF arrives.</summary>
    public decimal? ReceivedAmount { get; set; }

    /// <summary>ISO currency code (e.g. "USD", "TRY"). Null until known.</summary>
    public string? Currency { get; set; }

    /// <summary>Invoice number from the PDF. Null until the PDF arrives.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>URL of the collected PDF (filled by the e-mail collector in a later phase). Null until collected.</summary>
    public string? PdfUrl { get; set; }

    /// <summary>Day of the month (in the month following the period) by which the invoice is due. Default 7.</summary>
    public int DueDay { get; set; } = 7;

    /// <summary>UTC timestamp when this record first entered <see cref="VendorInvoiceStatus.Expected"/>.</summary>
    public DateTime? ExpectedOn { get; set; }

    /// <summary>UTC timestamp when the PDF was received (MarkReceived).</summary>
    public DateTime? ReceivedOn { get; set; }

    /// <summary>UTC timestamp when a Missing alarm was raised for this record.</summary>
    public DateTime? AlertedOn { get; set; }

    /// <summary>Free-text notes (mismatch reason, manual annotations, …).</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The date the invoice is due: first day of the period month + 1 month + (DueDay - 1) days.
    /// DueDay=7 ⇒ the 7th of the month following the period. If <c>asOf &gt; DueDate</c> and still
    /// Expected, the record is considered Missing.
    /// </summary>
    public DateTime DueDate()
    {
        var firstOfPeriod = new DateTime(PeriodYear, PeriodMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        return firstOfPeriod.AddMonths(1).AddDays(DueDay - 1);
    }
}
