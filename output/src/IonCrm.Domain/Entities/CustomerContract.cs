using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a recurring monthly subscription contract for a customer.
/// Currently used for RezervAl customers — created/renewed via the
/// <c>POST /api/v1/customers/{id}/contracts</c> endpoint, which also calls
/// the Rezerval subscription endpoint to create an iyzico subscription
/// + payment plan on the Rezerval side.
///
/// For <see cref="ContractPaymentType.EftWire"/> contracts, a background job
/// (<c>SyncRezervalContractInvoicesCommand</c>) creates a monthly draft invoice
/// on each <see cref="NextInvoiceDate"/>, advancing the date by one month after
/// each successful generation.
/// </summary>
public class CustomerContract : BaseEntity
{
    /// <summary>Gets or sets the tenant (project) this contract belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the customer this contract is for.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable contract title.
    /// Auto-generated as "{CompanyName} Abonelik".
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the monthly subscription amount in TRY.</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>Gets or sets the payment method chosen for this contract.</summary>
    public ContractPaymentType PaymentType { get; set; }

    /// <summary>
    /// Gets or sets the contract start date (UTC midnight).
    /// Also determines the day-of-month used to schedule recurring invoices.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the optional contract duration in months.
    /// <c>null</c> means the contract runs indefinitely until cancelled.
    /// </summary>
    public int? DurationMonths { get; set; }

    /// <summary>
    /// Gets or sets the calculated end date (UTC midnight).
    /// <c>null</c> when <see cref="DurationMonths"/> is null (indefinite contract).
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Gets or sets the contract lifecycle status.</summary>
    public ContractStatus Status { get; set; } = ContractStatus.Active;

    /// <summary>
    /// Gets or sets the iyzico subscription reference returned by the Rezerval endpoint.
    /// Populated only when the Rezerval call succeeds.
    /// </summary>
    public string? RezervalSubscriptionId { get; set; }

    /// <summary>
    /// Gets or sets the iyzico payment plan reference returned by the Rezerval endpoint.
    /// Populated only when the Rezerval call succeeds.
    /// </summary>
    public string? RezervalPaymentPlanId { get; set; }

    /// <summary>
    /// Gets or sets the date the next monthly draft invoice should be generated (UTC midnight).
    /// Used by the background sync job — only EFT contracts have this populated.
    /// Set to <c>null</c> for credit-card contracts and after the contract completes.
    /// </summary>
    public DateTime? NextInvoiceDate { get; set; }

    /// <summary>
    /// Gets or sets the date of the most recently generated draft invoice (UTC midnight).
    /// Audit field — null until the first invoice is generated.
    /// </summary>
    public DateTime? LastInvoiceGeneratedDate { get; set; }

    // ── Navigation properties ─────────────────────────────────────────────────

    /// <summary>Gets or sets the project (tenant) navigation property.</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the customer navigation property.</summary>
    public Customer Customer { get; set; } = null!;
}
