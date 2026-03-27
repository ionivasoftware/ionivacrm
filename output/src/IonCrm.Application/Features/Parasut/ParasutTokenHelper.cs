using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut;

/// <summary>
/// Shared helper that ensures a <see cref="ParasutConnection"/> has a valid access token.
///
/// Three-tier recovery strategy:
///   1. Token still valid              → use as-is
///   2. Token expired + refresh token  → silent refresh via refresh_token grant
///   3. Refresh fails or no refresh    → full re-authentication via password grant
///                                       using the stored credentials (ClientId, ClientSecret, Username, Password)
/// </summary>
public static class ParasutTokenHelper
{
    /// <summary>
    /// Returns the connection with a guaranteed-valid access token, or null with an error message.
    /// </summary>
    public static async Task<(ParasutConnection? Connection, string? Error)> EnsureValidTokenAsync(
        ParasutConnection? connection,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (connection is null)
            return (null, "Paraşüt bağlantısı bulunamadı. Lütfen Ayarlar'dan Paraşüt'e bağlanın.");

        // 1 ── Token still valid
        if (connection.IsConnected)
            return (connection, null);

        // 2 ── Token expired — try refresh_token grant first
        if (!string.IsNullOrEmpty(connection.RefreshToken))
        {
            try
            {
                logger.LogInformation(
                    "Paraşüt access token expired for project {ProjectId}. Attempting refresh...",
                    connection.ProjectId);

                var refreshed = await parasutClient.RefreshTokenAsync(
                    connection.RefreshToken,
                    connection.ClientId,
                    connection.ClientSecret,
                    cancellationToken);

                ApplyToken(connection, refreshed);
                await connectionRepository.UpdateAsync(connection, cancellationToken);

                logger.LogInformation(
                    "Paraşüt token refreshed for project {ProjectId}. New expiry: {Expiry:u}",
                    connection.ProjectId, connection.TokenExpiresAt);

                return (connection, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Paraşüt refresh_token grant failed for project {ProjectId}. Falling back to password grant.",
                    connection.ProjectId);
            }
        }

        // 3 ── Refresh failed or no refresh token — full re-authentication with stored credentials
        if (string.IsNullOrEmpty(connection.Username) || string.IsNullOrEmpty(connection.Password))
        {
            return (null,
                "Paraşüt token süresi dolmuş ve yeniden giriş için kayıtlı bilgiler yok. Lütfen Ayarlar'dan tekrar bağlanın.");
        }

        try
        {
            logger.LogInformation(
                "Paraşüt re-authenticating with stored credentials for project {ProjectId}.",
                connection.ProjectId);

            var newToken = await parasutClient.GetTokenAsync(
                new ParasutTokenRequest(
                    GrantType:    "password",
                    ClientId:     connection.ClientId,
                    ClientSecret: connection.ClientSecret,
                    Username:     connection.Username,
                    Password:     connection.Password),
                cancellationToken);

            ApplyToken(connection, newToken);
            await connectionRepository.UpdateAsync(connection, cancellationToken);

            logger.LogInformation(
                "Paraşüt re-authenticated for project {ProjectId}. New expiry: {Expiry:u}",
                connection.ProjectId, connection.TokenExpiresAt);

            return (connection, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Paraşüt re-authentication failed for project {ProjectId}.", connection.ProjectId);
            return (null,
                "Paraşüt'e otomatik bağlanılamadı. Lütfen Ayarlar'dan şifreyi kontrol edip tekrar bağlanın.");
        }
    }

    private static void ApplyToken(ParasutConnection connection, ParasutTokenResponse token)
    {
        connection.AccessToken    = token.AccessToken;
        connection.RefreshToken   = token.RefreshToken;
        connection.TokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    }
}
