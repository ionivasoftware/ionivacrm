namespace IonCrm.Domain.Enums;

/// <summary>
/// What a calendar appointment is for. The three call kinds are the CSO planner's daily quotas;
/// the rest are manual (customer detail → "Randevu oluştur"). Integer values persist — never renumber.
/// </summary>
public enum AppointmentKind
{
    /// <summary>Yeni arama — first/next outreach to a Lead.</summary>
    NewCall = 1,

    /// <summary>Takip araması — existing customer follow-up (usage, renewal, promised callback).</summary>
    FollowUpCall = 2,

    /// <summary>Memnuniyet araması — relationship call to a healthy, long-untouched customer.</summary>
    SatisfactionCall = 3,

    Meeting = 4,

    Training = 5,

    Demo = 6,

    Other = 7,
}

public enum AppointmentSource
{
    Manual = 1,

    /// <summary>Created by the CSO planner (rule-based, reason recorded on the row).</summary>
    Agent = 2,
}

public enum AppointmentStatus
{
    Planned = 1,

    /// <summary>Contact logged; linked ContactHistory row exists.</summary>
    Completed = 2,

    /// <summary>Operator decided not to do it (reason optional).</summary>
    Skipped = 3,

    /// <summary>Attempted, customer not reached; a contact row is still logged (Reached=false).</summary>
    NoAnswer = 4,
}
