using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A lift company outside Türkiye found by the global lead agent — kept apart from <see cref="Customer"/>
/// (that table stays TR-only). Review flow mirrors <see cref="LeadCandidate"/>; on approval an e-mail
/// outreach sequence (intro + timed follow-ups, from the approving user's Gmail) starts and is tracked
/// here: step, next send time, replies/bounces/unsubscribe. Global, SuperAdmin-only.
/// </summary>
public class GlobalLead : BaseEntity
{
    /// <summary>ISO 3166-1 alpha-2, e.g. GB.</summary>
    public string Country { get; set; } = "GB";

    /// <summary>Region/city the agent was scanning (London, Greater Manchester…).</summary>
    public string? Region { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Email { get; set; }

    public string? Domain { get; set; }

    public string? Phone { get; set; }

    public string? NormalizedPhone { get; set; }

    public string? Website { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string? Snippet { get; set; }

    public int Confidence { get; set; }

    public GlobalLeadStatus Status { get; set; } = GlobalLeadStatus.Pending;

    public Guid? DuplicateOfLeadId { get; set; }

    public Guid? PossibleMatchLeadId { get; set; }

    public string? MatchNotes { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public string? AgentRunId { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }

    // ── Outreach ─────────────────────────────────────────────────────────────

    public OutreachStatus OutreachStatus { get; set; } = OutreachStatus.NotStarted;

    /// <summary>Index of the NEXT step to send (0 = intro not sent yet).</summary>
    public int OutreachStep { get; set; }

    public DateTime? NextSendAt { get; set; }

    public DateTime? LastSentAt { get; set; }

    public int SentCount { get; set; }

    /// <summary>User whose Gmail sends the whole sequence (so replies land in one inbox).</summary>
    public Guid? SenderUserId { get; set; }

    /// <summary>Message-Id of the intro — follow-ups reply in the same thread.</summary>
    public string? ThreadMessageId { get; set; }

    /// <summary>Opaque token in the unsubscribe link.</summary>
    public string UnsubscribeToken { get; set; } = string.Empty;

    public DateTime? RepliedAt { get; set; }

    public DateTime? BouncedAt { get; set; }

    public DateTime? UnsubscribedAt { get; set; }

    public string? LastError { get; set; }

    public string? Notes { get; set; }
}

/// <summary>One outbound e-mail of a global lead's sequence (audit + threading + daily caps).</summary>
public class GlobalLeadEmail : BaseEntity
{
    public Guid LeadId { get; set; }

    public int Step { get; set; }

    public string TemplateKey { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public Guid SenderUserId { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string? MessageId { get; set; }

    public OutreachEmailStatus Status { get; set; } = OutreachEmailStatus.Sent;

    public string? Error { get; set; }
}
