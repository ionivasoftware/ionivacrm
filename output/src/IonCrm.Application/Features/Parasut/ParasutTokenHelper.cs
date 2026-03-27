using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut;

/// <summary>
/// Shared helper that ensures a <see cref="ParasutConnection"/> has a valid access token.
/// If the token is expired but a refresh token is available, transparently obtains a new
/// access token via <see cref="IParasutClient.RefreshTokenAsync"/> and persists it.
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

        // Token still valid — nothing to do
        if (connection.IsConnected)
            return (connection, null);

        // Token expired — attempt refresh
        if (string.IsNullOrEmpty(connection.RefreshToken))
            return (null, "Paraşüt token süresi dolmuş ve yenileme token'ı bulunamadı. Lütfen Ayarlar'dan tekrar bağlanın.");

        try
        {
            logger.LogInformation(
                "Paraşüt access token expired for project {ProjectId}. Refreshing...",
                connection.ProjectId);

            var newToken = await parasutClient.RefreshTokenAsync(
                connection.RefreshToken,
                connection.ClientId,
                connection.ClientSecret,
                cancellationToken);

            connection.AccessToken    = newToken.AccessToken;
            connection.RefreshToken   = newToken.RefreshToken;
            connection.TokenExpiresAt = DateTime.UtcNow.AddSeconds(newToken.ExpiresIn - 60);

            await connectionRepository.UpdateAsync(connection, cancellationToken);

            logger.LogInformation(
                "Paraşüt token refreshed for project {ProjectId}. New expiry: {Expiry:u}",
                connection.ProjectId, connection.TokenExpiresAt);

            return (connection, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Paraşüt token refresh failed for project {ProjectId}.", connection.ProjectId);
            return (null,
                "Paraşüt token yenilenemedi. Lütfen Ayarlar'dan tekrar bağlanın.");
        }
    }
}
