using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A request for the customer's permission before an operator opens a support session ("Hesaba Gir") on one of
/// the customer's Liftdesk users. The customer answers through a tokenised link; an approval opens a time-boxed
/// window (<see cref="ValidHours"/>, default 2) during which the operator may log in. Tenant-scoped via ProjectId.
/// </summary>
public class SupportAccessRequest : BaseEntity
{
    public Guid CustomerId { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>Liftdesk user the session will be opened on.</summary>
    public string EmsUserId { get; set; } = string.Empty;

    public string? EmsUserName { get; set; }

    /// <summary>Address the request was e-mailed to.</summary>
    public string To { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>Opaque token in the approve/decline link (unique).</summary>
    public string Token { get; set; } = string.Empty;

    public Guid RequestedByUserId { get; set; }

    /// <summary>Operator e-mail (the same identity Liftdesk records as the impersonation agent).</summary>
    public string RequestedBy { get; set; } = string.Empty;

    public string RequestedByName { get; set; } = string.Empty;

    /// <summary>How long an approval stays valid, counted from the customer's answer.</summary>
    public int ValidHours { get; set; } = 2;

    /// <summary>Language the request mail was written in ("tr" / "en"); the public answer pages reuse it. Null on rows from before the column existed.</summary>
    public string? Language { get; set; }

    public SupportAccessStatus Status { get; set; } = SupportAccessStatus.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The link stops working after this (unanswered requests go stale).</summary>
    public DateTime LinkExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    /// <summary>End of the login window (RespondedAt + ValidHours) when approved.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? FirstUsedAt { get; set; }

    /// <summary>Support sessions opened under this approval.</summary>
    public int UseCount { get; set; }

    public string? MessageId { get; set; }

    public string? SendError { get; set; }
}
