using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// JWT access-token generation and refresh-token lifecycle management.
/// Access tokens: signed JWTs (15 min). Refresh tokens: opaque, SHA-256 hashed in DB (7 days).
/// NEVER logs raw tokens or secrets.
/// </summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TokenService> _logger;

    /// <summary>Initialises a new instance of <see cref="TokenService"/>.</summary>
    public TokenService(
        IConfiguration configuration,
        ApplicationDbContext dbContext,
        ILogger<TokenService> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
    }

    // ── Access token ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string GenerateAccessToken(User user)
    {
        var key = GetSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 15);

        // Build project-roles JSON: { "projectId": "RoleName" }
        var rolesDict = user.UserProjectRoles
            .Where(upr => !upr.IsDeleted)
            .ToDictionary(
                upr => upr.ProjectId.ToString(),
                upr => upr.Role.ToString());

        var projectIds = string.Join(',',
            user.UserProjectRoles
                .Where(upr => !upr.IsDeleted)
                .Select(upr => upr.ProjectId.ToString()));

        var claims = new List<Claim>
        {
            new Claim("userId",      user.Id.ToString()),
            new Claim("email",       user.Email),
            new Claim("isSuperAdmin", user.IsSuperAdmin.ToString().ToLower()),
            new Claim("projectIds",  projectIds),
            new Claim("roles",       JsonSerializer.Serialize(rolesDict)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                      DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                      ClaimValueTypes.Integer64)
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject            = new ClaimsIdentity(claims),
            Expires            = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Issuer             = _configuration["Jwt:Issuer"] ?? "IonCrm",
            Audience           = _configuration["Jwt:Audience"] ?? "IonCrm",
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token   = handler.CreateToken(descriptor);

        _logger.LogDebug("Access token generated for user {UserId}", user.Id);

        return handler.WriteToken(token);
    }

    // ── Refresh token ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(string RawToken, RefreshToken Entity)> CreateRefreshTokenAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var expiryDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpiryDays", 7);

        // Generate a cryptographically secure random raw token (256 bits)
        var rawBytes  = RandomNumberGenerator.GetBytes(32);
        var rawToken  = Convert.ToBase64String(rawBytes);
        var tokenHash = HashToken(rawToken);

        var entity = new RefreshToken
        {
            UserId    = user.Id,
            Token     = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            IsRevoked = false
        };

        _dbContext.RefreshTokens.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Refresh token created for user {UserId}", user.Id);

        return (rawToken, entity);
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetActiveRefreshTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(rawToken);

        return await _dbContext.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(rt =>
                rt.Token      == hash    &&
                !rt.IsRevoked            &&
                !rt.IsDeleted            &&
                rt.ExpiresAt  > DateTime.UtcNow,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeRefreshTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var hash   = HashToken(rawToken);
        var entity = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == hash && !rt.IsDeleted, cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning("Attempted to revoke a refresh token that was not found");
            return;
        }

        entity.IsRevoked = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Refresh token revoked for user {UserId}", entity.UserId);
    }

    /// <inheritdoc />
    public async Task RevokeAllUserRefreshTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && !rt.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
            token.IsRevoked = true;

        if (tokens.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Revoked {Count} refresh token(s) for user {UserId}", tokens.Count, userId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private SymmetricSecurityKey GetSigningKey()
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT signing key is missing. Set the 'Jwt:Key' configuration value.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    }

    /// <summary>Produces a hex-encoded SHA-256 hash of the raw token value.</summary>
    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
