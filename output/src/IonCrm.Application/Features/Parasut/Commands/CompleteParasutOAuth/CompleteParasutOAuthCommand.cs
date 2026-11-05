using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.CompleteParasutOAuth;

/// <summary>
/// Paraşüt's browser redirect back to us: <c>code</c> + <c>state</c> (or <c>error</c>). Anonymous — the only trust anchor is the
/// single-use <c>state</c>, matched by its hash. Never throws: the outcome is carried to the settings page as a code.
/// </summary>
public sealed record CompleteParasutOAuthCommand(string? Code, string? State, string? Error) : IRequest<ParasutOAuthCallbackResult>;

/// <summary><c>connected</c>, or <c>error</c> with a reason code (state / expired / denied / parasut / code / verifier / exchange / company).</summary>
public sealed record ParasutOAuthCallbackResult(string Outcome, string? Reason)
{
    public bool IsConnected => Outcome == ParasutOAuthFlow.OutcomeConnected;

    public static ParasutOAuthCallbackResult Connected() => new(ParasutOAuthFlow.OutcomeConnected, null);

    public static ParasutOAuthCallbackResult Fail(string reason) => new(ParasutOAuthFlow.OutcomeError, reason);
}
