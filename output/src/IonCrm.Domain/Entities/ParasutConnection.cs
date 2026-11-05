using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Stores the OAuth 2.0 credentials and tokens for a project's Paraşüt accounting integration.
/// One connection per project (tenant). Tokens are refreshed automatically by <c>IParasutClient</c>.
/// </summary>
public class ParasutConnection : BaseEntity
{
    /// <summary>
    /// Gets or sets the project (tenant) this connection belongs to.
    /// <c>null</c> means this is the global Paraşüt connection shared by all projects.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Gets or sets the Paraşüt company (firma) numeric identifier. On the new flow it comes from <c>/v4/me</c>, not from the user.</summary>
    public long CompanyId { get; set; }

    /// <summary>Company name as Paraşüt reports it (new flow, from <c>/v4/me</c>); null on legacy rows.</summary>
    public string? CompanyName { get; set; }

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

    // ── Reconnect tracking ───────────────────────────────────────────────────

    /// <summary>Gets or sets the UTC time when the last successful token acquisition occurred.</summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>Gets or sets the last error message from a failed reconnect attempt (null when healthy).</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the number of consecutive failed reconnect attempts (resets to 0 on success).</summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>When the "connection is dead, a human is needed" alert went out; null = not alerted (reset on success).</summary>
    public DateTime? ReconnectAlertedAt { get; set; }

    // ── Authorisation flow (Paraşüt 04–05.11.2026: password grant retires) ───

    /// <summary>Which flow authorised this row — decides the refresh path. Existing rows are <see cref="ParasutAuthFlow.Password"/>.</summary>
    public ParasutAuthFlow AuthFlow { get; set; } = ParasutAuthFlow.Password;

    /// <summary>Expiry of the refresh token (UTC; ~30 days on the new flow, reset by every refresh). Null on legacy rows.</summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// The refresh was rejected for good (token revoked/expired, access stopped from Paraşüt's "Uygulama Entegrasyonları", or the
    /// authorising user lost that permission). No password fallback exists on the new flow: a human must re-authorise.
    /// </summary>
    public bool NeedsReconnect { get; set; }

    /// <summary>When the authorisation completed (UTC) and by which CRM user — Paraşüt shows the same on its side.</summary>
    public DateTime? ConnectedAt { get; set; }

    public Guid? ConnectedByUserId { get; set; }

    // ── Open authorisation (single use) ───────────────────────────────────────

    /// <summary>SHA-256 (hex) of the pending <c>state</c>; the raw value is never stored.</summary>
    public string? OAuthStateHash { get; set; }

    public DateTime? OAuthStateExpiresAt { get; set; }

    /// <summary>User who started the authorisation — becomes <see cref="ConnectedByUserId"/> on success.</summary>
    public Guid? OAuthStateUserId { get; set; }

    /// <summary>PKCE code verifier, ENCRYPTED; used once in the code exchange and cleared.</summary>
    public string? OAuthCodeVerifier { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    /// <summary>Gets or sets the project navigation property. Null when this is the global connection.</summary>
    public Project? Project { get; set; }
}
