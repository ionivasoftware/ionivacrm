using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Stores Paraşüt product (ürün) configuration for invoice line items.
/// Each project can configure 6 products: memberships (1-month, 1-year) + SMS packages (1000, 2500, 5000, 10000).
/// Maps CRM product names to Paraşüt product IDs, unit prices, and tax rates.
/// </summary>
public class ParasutProduct : BaseEntity
{
    /// <summary>Gets or sets the project (tenant) this product belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the product name (e.g., "1 Aylık Üyelik", "1000 SMS").
    /// Used as a unique key within the project.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Paraşüt product ID (numeric ID from Paraşüt API).
    /// This is the "relationships.product.data.id" field when creating sales invoice details.
    /// </summary>
    public string ParasutProductId { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable Paraşüt product name for display.</summary>
    public string? ParasutProductName { get; set; }

    /// <summary>
    /// Gets or sets the unit price for this product (e.g., 299.00 for 1-month membership).
    /// Used as default price when creating invoices.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the tax rate (KDV oranı) as a decimal (e.g., 0.20 for 20% tax).
    /// Used when calculating invoice totals.
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Gets or sets the EMS product ID used to match incoming EMS payments to this CRM product.
    /// When an EMS payment arrives with a matching ProductId, this product's price/tax settings
    /// are used to auto-generate the invoice draft line item.
    /// </summary>
    public string? EmsProductId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    /// <summary>Gets or sets the project navigation property.</summary>
    public Project Project { get; set; } = null!;
}
