using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A company the lead-discovery agent found on the web, waiting for SuperAdmin approval before it
/// becomes a <see cref="Customer"/> (Status=Lead). Dedupe happens on arrival: name / phone / domain /
/// tax number are normalised and compared with every existing customer and candidate, so the same
/// firm is never proposed twice (rejected ones keep their fingerprint for that reason).
/// </summary>
public class LeadCandidate : BaseEntity
{
    /// <summary>Target project when approved (default: the Liftdesk project).</summary>
    public Guid ProjectId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Legal title as found (ünvan), e.g. "X Asansör San. ve Tic. Ltd. Şti."</summary>
    public string? LegalTitle { get; set; }

    public string? Phone { get; set; }

    /// <summary>Digits only, national 10-digit form (5xx… / 2xx…).</summary>
    public string? NormalizedPhone { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    /// <summary>Registrable host of website/email, generic mail providers excluded.</summary>
    public string? Domain { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? District { get; set; }

    public string? TaxNumber { get; set; }

    public string? MersisNo { get; set; }

    /// <summary>Where it was found: google | armut | directory:&lt;host&gt; | website.</summary>
    public string Source { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string? Snippet { get; set; }

    /// <summary>Agent's confidence 0–100 that this is an operating elevator company.</summary>
    public int Confidence { get; set; }

    public LeadCandidateStatus Status { get; set; } = LeadCandidateStatus.Pending;

    /// <summary>Set when dedupe found a definite match (status Duplicate).</summary>
    public Guid? DuplicateOfCustomerId { get; set; }

    /// <summary>Set when dedupe found a *possible* match (fuzzy name) — stays Pending, operator decides.</summary>
    public Guid? PossibleMatchCustomerId { get; set; }

    public string? MatchNotes { get; set; }

    /// <summary>sha256(normalizedName|normalizedPhone|domain) — unique, blocks re-submission.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public string? AgentRunId { get; set; }

    /// <summary>Province the agent was scanning when it found this.</summary>
    public string? SearchCity { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }

    /// <summary>Customer created on approval.</summary>
    public Guid? CreatedCustomerId { get; set; }
}

/// <summary>One agent run (GitHub Actions job) — which provinces, how many found/created/duplicate.</summary>
public class LeadAgentRun : BaseEntity
{
    public string RunId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    /// <summary>Comma-separated provinces scanned.</summary>
    public string Cities { get; set; } = string.Empty;

    public int Found { get; set; }

    public int Created { get; set; }

    public int Duplicates { get; set; }

    public int Existing { get; set; }

    public int Invalid { get; set; }

    public string? Notes { get; set; }
}
