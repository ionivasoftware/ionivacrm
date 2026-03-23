namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Response payload returned after a successful login or token refresh.
/// The client must store the refresh token securely (HttpOnly cookie recommended for web).
/// </summary>
public class AuthResponseDto
{
    /// <summary>Gets or sets the signed JWT access token. TTL: 15 minutes.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the opaque refresh token (raw value). TTL: 7 days.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp when the access token expires.</summary>
    public DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>Gets or sets summary information about the authenticated user.</summary>
    public UserDto User { get; set; } = null!;
}
