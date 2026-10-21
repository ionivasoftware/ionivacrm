namespace IonCrm.Domain.Enums;

public enum GlobalLeadStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Duplicate = 4,
}

/// <summary>E-mail sequence state of an approved global lead.</summary>
public enum OutreachStatus
{
    NotStarted = 1,
    /// <summary>Sequence running — next step waits for NextSendAt.</summary>
    Active = 2,
    /// <summary>The lead wrote back (IMAP detected or marked by hand) — sequence stopped, human takes over.</summary>
    Replied = 3,
    Bounced = 4,
    Unsubscribed = 5,
    /// <summary>All steps sent, no reply.</summary>
    Completed = 6,
    Paused = 7,
    /// <summary>Sending failed repeatedly (sender settings missing, SMTP rejected) — needs attention.</summary>
    Failed = 8,
}

public enum OutreachEmailStatus
{
    Sent = 1,
    Failed = 2,
}
