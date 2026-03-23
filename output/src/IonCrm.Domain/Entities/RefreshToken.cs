using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Stores issued refresh tokens. Tokens are stored as SHA-256 hashes — never raw.
/// Access token TTL: 15 minutes. Refresh token TTL: 7 days.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>Gets or sets the user this refresh token belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 hash of the token string.
    /// NEVER store the raw token value.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC expiry timestamp for this token.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Gets or sets a value indicating whether this token has been revoked.</summary>
    public bool IsRevoked { get; set; } = false;

    /// <summary>Gets a value indicating whether this token is expired.</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>Gets a value indicating whether this token is currently active and usable.</summary>
    public bool IsActive => !IsRevoked && !IsExpired && !IsDeleted;

    // Navigation properties
    /// <summary>Gets or sets the user who owns this token.</summary>
    public User User { get; set; } = null!;
}
