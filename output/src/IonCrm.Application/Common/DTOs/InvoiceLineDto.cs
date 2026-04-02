namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Represents a single line item on an invoice.
/// Stored denormalized as a JSON array in <c>Invoice.LinesJson</c>.
/// </summary>
public class InvoiceLineDto
{
    /// <summary>Line description / product name.</summary>
    public string? Description { get; set; }

    /// <summary>Quantity of items.</summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>Unit price (per item, before discount).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>VAT rate as an integer percentage (e.g. 20 = 20%).</summary>
    public int VatRate { get; set; }

    /// <summary>
    /// Discount value.
    /// Interpretation depends on <see cref="DiscountType"/>:
    ///   "percent" → percentage off the line subtotal (e.g. 10 = 10%)
    ///   "amount"  → fixed currency amount deducted from the line subtotal
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Discount type.
    /// Accepted values: <c>"percent"</c> (default) or <c>"amount"</c>.
    /// </summary>
    public string DiscountType { get; set; } = "percent";

    /// <summary>Unit label (e.g. "Adet", "Saat", "Kg"). Defaults to "Adet".</summary>
    public string? Unit { get; set; }

    /// <summary>Optional linked Paraşüt product ID for product-level sync.</summary>
    public string? ParasutProductId { get; set; }
}
