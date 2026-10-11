using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// One publish attempt chain for (post × account). Unique on that pair — the database, not the
/// scheduler, guarantees a platform is never posted to twice for the same post. Keeps the platform's
/// post id for audit, later deletion or statistics.
/// </summary>
public class SocialPublishJob : BaseEntity
{
    public Guid PostId { get; set; }

    public Guid AccountId { get; set; }

    public SocialProvider Provider { get; set; }

    /// <summary>Variant language sent (the account's mapping at scheduling time).</summary>
    public string Language { get; set; } = string.Empty;

    public SocialPublishJobStatus Status { get; set; } = SocialPublishJobStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>Earliest time the next attempt may run (backoff); null = as soon as possible.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Platform-side id of the created post (FB post id, IG media id, X tweet id).</summary>
    public string? PlatformPostId { get; set; }

    /// <summary>Public image URL handed to the platform (custom image or stored template card).</summary>
    public string? ImageUrl { get; set; }
}
