using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a sales opportunity (deal) linked to a customer.
/// </summary>
public class Opportunity : BaseEntity
{
    /// <summary>Gets or sets the customer this opportunity is linked to.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the project (tenant) identifier (denormalized).</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the opportunity title or deal name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the monetary value of the opportunity.</summary>
    public decimal? Value { get; set; }

    /// <summary>Gets or sets the current pipeline stage.</summary>
    public OpportunityStage Stage { get; set; } = OpportunityStage.YeniArama;

    /// <summary>
    /// Gets or sets the probability of closing this opportunity (0–100).
    /// </summary>
    public int? Probability { get; set; }

    /// <summary>Gets or sets the expected close date.</summary>
    public DateOnly? ExpectedCloseDate { get; set; }

    /// <summary>Gets or sets the sales rep responsible for this opportunity.</summary>
    public Guid? AssignedUserId { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the associated customer.</summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the assigned user.</summary>
    public User? AssignedUser { get; set; }
}
