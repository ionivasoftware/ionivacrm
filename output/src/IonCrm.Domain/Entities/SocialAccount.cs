using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A connected social-media account the publisher posts to (Liftdesk's own marketing accounts).
/// Tokens live ONLY here, encrypted at rest (<c>ITokenProtector</c>); Liftdesk/EMS never sees them.
/// Infrastructure-wide, no tenant scope. Several account sets can coexist (TR today, UK later) —
/// <see cref="Language"/> decides which post variant each account receives.
/// </summary>
public class SocialAccount : BaseEntity
{
    public SocialProvider Provider { get; set; }

    /// <summary>Operator-facing label, e.g. "Liftdesk TR — Facebook".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Variant language this account publishes (tr | en | en-GB); null = unmapped, never publishes.</summary>
    public string? Language { get; set; }

    /// <summary>Page id (Facebook), IG user id (Instagram) or user id (X).</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Page name / IG username / X @handle.</summary>
    public string? ExternalName { get; set; }

    /// <summary>Instagram only: the Facebook Page whose token is used.</summary>
    public string? ParentExternalId { get; set; }

    public string AccessTokenEncrypted { get; set; } = string.Empty;

    /// <summary>X only (OAuth 2.0 offline.access). Meta page tokens are long-lived without refresh.</summary>
    public string? RefreshTokenEncrypted { get; set; }

    /// <summary>Access-token expiry when the platform reports one; null = long-lived/unknown.</summary>
    public DateTime? TokenExpiresAt { get; set; }

    public string? Scopes { get; set; }

    public SocialAccountStatus Status { get; set; } = SocialAccountStatus.Active;

    public string? LastError { get; set; }

    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>Disabled accounts are skipped by the scheduler but keep their token.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>E-mail of the CRM user who completed the OAuth flow (audit).</summary>
    public string? ConnectedBy { get; set; }
}
