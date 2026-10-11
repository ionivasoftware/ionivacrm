using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// The CRM's own record of an approved Liftdesk post that entered the publishing pipeline — one row
/// per EMS post id. Holds the stamp-back bookkeeping (<see cref="StampedAt"/>) and a note when the
/// post was deliberately not published (stale, no mapped account). Jobs hang off <see cref="PostId"/>.
/// </summary>
public class SocialPublishPost : BaseEntity
{
    /// <summary>EMS SocialMediaPost id.</summary>
    public Guid PostId { get; set; }

    /// <summary>Publish DAY (00:00Z) as EMS holds it.</summary>
    public DateTime ScheduledFor { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>When every job reached a terminal success/skip state.</summary>
    public DateTime? AllDoneAt { get; set; }

    /// <summary>When EMS accepted <c>POST {id}/published</c>.</summary>
    public DateTime? StampedAt { get; set; }

    public string? StampError { get; set; }

    public int StampAttempts { get; set; }

    /// <summary>Operator-facing reason when nothing was scheduled (stale post, no mapped account).</summary>
    public string? Note { get; set; }
}
