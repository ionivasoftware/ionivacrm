using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Stores the OAuth 2.0 credentials and tokens for a project's Paraşüt accounting integration.
/// One connection per project (tenant). Tokens are refreshed automatically by <c>IParasutClient</c>.
/// </summary>
public class ParasutConnection : BaseEntity
{
    /// <summary>Gets or sets the project (tenant) this connection belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the Paraşüt company (firma) numeric identifier.</summary>
    public long CompanyId { get; set; }

    /// <summary>Gets or sets the OAuth client ID provided by Paraşüt.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the OAuth client secret provided by Paraşüt.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the Paraşüt account e-mail (used for password grant).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Paraşüt account password.
    /// Stored as-is — this is an API service credential, not a user password for this system.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    // ── OAuth tokens (auto-refreshed) ─────────────────────────────────────────

    /// <summary>Gets or sets the current Bearer access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Gets or sets the refresh token used to obtain a new access token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Gets or sets the UTC expiry time of the current access token.</summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// Gets a value indicating whether the connection has a valid, non-expired access token.
    /// </summary>
    public bool IsConnected =>
        !string.IsNullOrEmpty(AccessToken) &&
        TokenExpiresAt.HasValue &&
        TokenExpiresAt.Value > DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────
    /// <summary>Gets or sets the project navigation property.</summary>
    public Project Project { get; set; } = null!;
}
