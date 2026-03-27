using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a customer or lead within a project (tenant).
/// Migrated from: EMS.dbo.Companies (status=Active) and dbo.PotentialCustomers (status=Lead).
/// </summary>
public class Customer : BaseEntity
{
    /// <summary>Gets or sets the tenant (project) this customer belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets an optional internal customer code.</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the company or individual name.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the primary contact person's name.</summary>
    public string? ContactName { get; set; }

    /// <summary>Gets or sets the contact email address.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the contact phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Gets or sets the physical address.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets the tax identification number.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>Gets or sets the tax office name.</summary>
    public string? TaxUnit { get; set; }

    /// <summary>Gets or sets the customer lifecycle status.</summary>
    public CustomerStatus Status { get; set; } = CustomerStatus.Lead;

    /// <summary>
    /// Gets or sets the business segment classification.
    /// Project-specific string value (e.g. "Asansör Firması" for EMS, "Tekil Restoran" for Rezerval).
    /// </summary>
    public string? Segment { get; set; }

    /// <summary>
    /// Gets or sets the quality/potential label for this customer.
    /// Maps to: YuksekPotansiyel, Potansiyel, Notr, Vasat, Kotu.
    /// </summary>
    public CustomerLabel? Label { get; set; }

    /// <summary>Gets or sets the subscription expiration date (synced from SaaS).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Gets or sets the sales rep assigned to this customer.</summary>
    public Guid? AssignedUserId { get; set; }

    /// <summary>
    /// Gets or sets the legacy ID from the old crm.bak database.
    /// Used for idempotent migration — format: numeric for Companies, "PC-{id}" for PotentialCustomers.
    /// </summary>
    public string? LegacyId { get; set; }

    /// <summary>
    /// Gets or sets the linked Paraşüt contact ID.
    /// Set when the customer is synced to or manually linked from Paraşüt.
    /// </summary>
    public string? ParasutContactId { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the assigned sales representative.</summary>
    public User? AssignedUser { get; set; }

    /// <summary>Gets or sets all contact history records for this customer.</summary>
    public ICollection<ContactHistory> ContactHistories { get; set; } = new List<ContactHistory>();

    /// <summary>Gets or sets all tasks associated with this customer.</summary>
    public ICollection<CustomerTask> Tasks { get; set; } = new List<CustomerTask>();

    /// <summary>Gets or sets all opportunities associated with this customer.</summary>
    public ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();
}
