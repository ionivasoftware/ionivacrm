namespace IonCrm.Application.Common.Models;

/// <summary>
/// A platform call failed. <see cref="IsPermanent"/> decides the job's fate: permanent → Blocked
/// (human), transient → Failed with backoff. <see cref="InvalidatesToken"/> additionally marks the
/// account Revoked (token rejected) so every other job on that account stops hammering the API.
/// </summary>
public sealed class SocialPublishException : Exception
{
    public SocialPublishException(string message, bool isPermanent, bool invalidatesToken = false, string? providerCode = null, Exception? inner = null)
        : base(message, inner)
    {
        IsPermanent = isPermanent;
        InvalidatesToken = invalidatesToken;
        ProviderCode = providerCode;
    }

    public bool IsPermanent { get; }

    public bool InvalidatesToken { get; }

    public string? ProviderCode { get; }

    /// <summary>Permission/tier problem on the account (not the token): account → Error, human checks permissions.</summary>
    public bool IsAccountFault { get; init; }
}

public record MetaTokenResult(string AccessToken, DateTime? ExpiresAt);

public record MetaPage(string Id, string Name, string AccessToken, string? InstagramId, string? InstagramUsername);

public record XTokenResult(string AccessToken, string? RefreshToken, DateTime? ExpiresAt, string? Scope);

public record XUser(string Id, string Username, string Name);

/// <summary>What one scheduler cycle did — shown on the publisher screen.</summary>
public record SocialPublishRunSummary(
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Ran,
    int PostsSeen,
    int JobsCreated,
    int JobsSucceeded,
    int JobsFailed,
    int JobsBlocked,
    int Stamped,
    List<string> Notes,
    string? Error);
