using IonCrm.Application.Features.Outreach;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Interfaces;

public record OutreachRunSummary(DateTime StartedAt, DateTime FinishedAt, int Due, int Sent, int Replied, int Bounced, int Failed, int Deferred, List<string> Notes);

public record InboxCheck(bool Replied, bool Bounced, string? Detail);

/// <summary>The company mailbox the sequence goes out from; <see cref="InboxCheck"/> = its IMAP side is known, so replies/bounces are detected.</summary>
public record OutreachSender(string Address, string Source, bool InboxCheck);

/// <summary>
/// Runs the e-mail outreach sequence for approved global leads. Sender: the shared company mailbox when configured
/// (<c>Outreach:Smtp:*</c> → <c>EmailCollector:Imap:*</c> → <c>Notifications:Smtp:*</c>), otherwise the approving user's own Gmail.
/// </summary>
public interface IOutreachService
{
    SequenceDefinition GetSequence();

    /// <summary>The sequence as it applies to a lead's country: its time zone, its template culture when those files exist (<see cref="Features.Outreach.OutreachPolicy.ForCountry"/>).</summary>
    SequenceDefinition GetSequenceFor(string? country);

    /// <summary>Shared company mailbox, or null when every approver sends from their own Gmail.</summary>
    OutreachSender? SharedSender { get; }

    /// <summary>Materials folder content for the screen: step templates (with subjects) and attachments.</summary>
    (List<(string Template, int DelayDays, bool Attachments, List<string> Subjects, bool Exists)> Steps, List<string> Attachments, string Folder) GetMaterials();

    /// <summary>Sends the lead's next step now (ignores window/cap); updates the lead and the log. Returns null on success, else the error.</summary>
    Task<string?> SendNextStepAsync(GlobalLead lead, CancellationToken cancellationToken = default);

    /// <summary>IMAP look for a reply from the lead or a bounce for the lead's address since the last send (best-effort).</summary>
    Task<InboxCheck> CheckInboxAsync(GlobalLead lead, CancellationToken cancellationToken = default);

    /// <summary>One scheduler cycle: due leads → inbox check → send within window and cap.</summary>
    Task<OutreachRunSummary> RunDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Process-wide last outreach cycle (singleton) for the screen.</summary>
public interface IOutreachState
{
    OutreachRunSummary? LastRun { get; set; }
}
