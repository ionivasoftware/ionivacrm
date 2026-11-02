using IonCrm.Application.Features.Lifecycle;
using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.Interfaces;

public record LifecycleRunSummary(DateTime StartedAt, DateTime FinishedAt, int Candidates, int Sent, int Failed, int Deferred, List<string> Notes);

public record LifecycleCandidate(Guid CustomerId, string CompanyName, string? Email, string? Country, string Language, CustomerStatus Status, DateTime CreatedAt, DateTime? ExpirationDate, LifecycleStep? DueStep, string? SkipReason, List<LifecycleStep> SentSteps, bool InWindow);

public record LifecycleStatus(bool Enabled, bool SmtpConfigured, string? From, string? SmtpSource, string? Folder, List<string> Languages, LifecycleOptions Options, Dictionary<string, int> SentByStep, LifecycleRunSummary? LastRun);

/// <summary>Outcome of a test send: <see cref="Error"/> null = delivered to the SMTP server.</summary>
public record LifecycleTestResult(string? Error, string To, string Language, string? Subject, string? From, string? SmtpSource);

/// <summary>Demo lifecycle e-mails for non-TR Liftdesk trial accounts (welcome → check-in → trial ended), sent from the company support mailbox.</summary>
public interface IDemoLifecycleService
{
    LifecycleOptions Options { get; }

    Task<LifecycleStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Every Liftdesk customer the rules look at (recent demos/passives), with the due step or the skip reason.</summary>
    Task<List<LifecycleCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Renders a step for a customer without sending (screen preview).</summary>
    Task<LifecycleTemplate.Rendered?> PreviewAsync(Guid customerId, LifecycleStep step, CancellationToken cancellationToken = default);

    /// <summary>Sends one step now. <paramref name="force"/> ignores eligibility/window but never re-sends a step already sent. Returns null on success, else the error.</summary>
    Task<string?> SendAsync(Guid customerId, LifecycleStep step, bool force, string triggeredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one step to an arbitrary address with sample data ("[TEST]" subject) — SMTP and template check.
    /// Nothing is logged against a customer. <paramref name="language"/> = template folder (default "en").
    /// </summary>
    Task<LifecycleTestResult> SendTestAsync(string to, LifecycleStep step, string? language, string triggeredBy, CancellationToken cancellationToken = default);

    /// <summary>One scheduler cycle.</summary>
    Task<LifecycleRunSummary> RunDueAsync(CancellationToken cancellationToken = default);
}

public interface IDemoLifecycleState
{
    LifecycleRunSummary? LastRun { get; set; }
}
