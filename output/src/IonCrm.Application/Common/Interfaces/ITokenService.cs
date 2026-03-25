using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Contract for JWT access-token and refresh-token generation, storage, and revocation.
/// Implementation lives in IonCrm.Infrastructure.Services.TokenService.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT access token for the given user.
    /// Claims include: userId, email, isSuperAdmin, projectIds (comma-separated), roles (JSON).
    /// TTL: 15 minutes (configured in JwtSettings).
    /// </summary>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Generates a cryptographically-secure refresh token, persists its SHA-256 hash to the database,
    /// and returns both the raw token (to be sent to the client) and the persisted entity.
    /// TTL: 7 days (configured in JwtSettings).
    /// </summary>
    Task<(string RawToken, RefreshToken Entity)> CreateRefreshTokenAsync(
        User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an active (non-revoked, non-expired, non-deleted) refresh token
    /// by its raw (unhashed) value. Returns null if not found or inactive.
    /// </summary>
    Task<RefreshToken?> GetActiveRefreshTokenAsync(
        string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the UTC timestamp when the next generated access token will expire,
    /// based on the configured TTL (JwtSettings:AccessTokenExpiryMinutes).
    /// </summary>
    DateTime GetAccessTokenExpiresAt();

    /// <summary>Marks a specific refresh token as revoked (soft-invalidation).</summary>
    Task RevokeRefreshTokenAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes ALL active refresh tokens for a user.
    /// Used during logout-everywhere or when a security event is detected.
    /// </summary>
    Task RevokeAllUserRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
