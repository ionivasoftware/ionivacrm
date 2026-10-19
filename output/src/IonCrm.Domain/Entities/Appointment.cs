using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A dated contact to make with a customer — the unit of the "Görüşme Takvimi" screen. Created by
/// hand (customer detail / calendar) or by the CSO planner (daily quotas of new / follow-up /
/// satisfaction calls, with the selection reason recorded so the operator sees WHY this company,
/// today). Completing one logs a <see cref="ContactHistory"/> row and links it here. Tenant-scoped.
/// </summary>
public class Appointment : BaseEntity
{
    public Guid ProjectId { get; set; }

    public Guid CustomerId { get; set; }

    public AppointmentKind Kind { get; set; }

    public AppointmentSource Source { get; set; } = AppointmentSource.Manual;

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Planned;

    /// <summary>UTC. Planner rows carry a slot time (10:00 / 14:00 / 16:30 TR) so the day reads as a schedule.</summary>
    public DateTime ScheduledAt { get; set; }

    public int DurationMinutes { get; set; } = 15;

    public string? Title { get; set; }

    /// <summary>Operator note (manual appointments).</summary>
    public string? Note { get; set; }

    /// <summary>Planner explanation, e.g. "Lead · Yüksek potansiyel · 45 gün görüşülmedi".</summary>
    public string? Reason { get; set; }

    /// <summary>Planner priority score (higher first); 0 for manual.</summary>
    public int Score { get; set; }

    public Guid? AssignedUserId { get; set; }

    /// <summary>The contact record logged when this appointment was completed / attempted.</summary>
    public Guid? ContactHistoryId { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? SkipReason { get; set; }

    public Guid? CreatedByUserId { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;

    public User? AssignedUser { get; set; }
}

/// <summary>One CSO planner run per (project, plan date) — idempotency marker and status for the screen.</summary>
public class CallPlanRun : BaseEntity
{
    public Guid ProjectId { get; set; }

    /// <summary>Local (TR) calendar day the run belongs to.</summary>
    public DateOnly PlanDate { get; set; }

    public int CreatedCount { get; set; }

    /// <summary>Human-readable summary (quotas filled per day, skipped reasons).</summary>
    public string? Summary { get; set; }

    public DateTime RanAt { get; set; } = DateTime.UtcNow;
}
