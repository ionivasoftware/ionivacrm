using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Records a single customer interaction — call, email, meeting, note, etc.
/// Migrated from: dbo.CustomerInterviews and dbo.AppointedInterviews.
/// </summary>
public class ContactHistory : BaseEntity
{
    /// <summary>Gets or sets the customer this interaction is with.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the project (tenant) identifier.
    /// Denormalized from Customer.ProjectId for efficient tenant filtering without JOIN.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the communication channel used.</summary>
    public ContactType Type { get; set; }

    /// <summary>Gets or sets a brief subject or topic for the interaction.</summary>
    public string? Subject { get; set; }

    /// <summary>Gets or sets the detailed notes or content of the interaction.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the outcome or result of the interaction.</summary>
    public string? Outcome { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the contact actually occurred.</summary>
    public DateTime ContactedAt { get; set; }

    /// <summary>Gets or sets the user who logged this interaction.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>What the contact was for (new / follow-up / satisfaction call…). Feeds the CSO planner.</summary>
    public AppointmentKind? Purpose { get; set; }

    /// <summary>Was the customer actually reached? false = no answer / busy (planner retries sooner).</summary>
    public bool? Reached { get; set; }

    /// <summary>Satisfaction calls: 1 (çok memnuniyetsiz) … 5 (çok memnun).</summary>
    public int? SatisfactionScore { get; set; }

    /// <summary>Promised callback ("şu tarihte tekrar arayın") — the planner schedules it on that day.</summary>
    public DateTime? NextContactAt { get; set; }

    /// <summary>The appointment this contact completed, when logged from the calendar.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Gets or sets the legacy ID from the old crm.bak database.
    /// Used for idempotent migration.
    /// </summary>
    public string? LegacyId { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the associated customer.</summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the user who created this record.</summary>
    public User? CreatedByUser { get; set; }
}
