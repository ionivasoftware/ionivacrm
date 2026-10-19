namespace IonCrm.Domain.Enums;

/// <summary>Lead-agent candidate review state.</summary>
public enum LeadCandidateStatus
{
    /// <summary>Waiting for SuperAdmin review.</summary>
    Pending = 1,

    /// <summary>Approved — a Customer (Lead) was created.</summary>
    Approved = 2,

    /// <summary>Rejected by a human; fingerprint kept so the agent cannot re-propose it.</summary>
    Rejected = 3,

    /// <summary>Dedupe matched an existing customer (or a human marked it so).</summary>
    Duplicate = 4,
}
