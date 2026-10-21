using IonCrm.Application.Features.Outreach;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Interfaces;

public record OutreachRunSummary(DateTime StartedAt, DateTime FinishedAt, int Due, int Sent, int Replied, int Bounced, int Failed, int Deferred, List<string> Notes);

public record InboxCheck(bool Replied, bool Bounced, string? Detail);

/// <summary>Runs the e-mail outreach sequence for approved global leads through the sender user's Gmail.</summary>
public interface IOutreachService
{
    SequenceDefinition GetSequence();

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
