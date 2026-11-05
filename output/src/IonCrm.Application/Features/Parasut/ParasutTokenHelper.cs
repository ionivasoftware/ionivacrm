using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut;

/// <summary>
/// Shared helper that ensures a <see cref="ParasutConnection"/> has a valid access token.
///
/// Legacy rows (<see cref="ParasutAuthFlow.Password"/>) keep the three-tier recovery:
///   1. Token still valid → use as-is · 2. refresh_token grant · 3. password grant with the stored credentials.
///
/// New-flow rows (<see cref="ParasutAuthFlow.OAuth2"/>) have NO password fallback — there is no password. A rejected refresh
/// marks <see cref="ParasutConnection.NeedsReconnect"/> and every caller gets the same "re-authorise" error until a human does;
/// a network error is left for the next cycle. Refresh tokens rotate: the one in the response is always the one stored.
///
/// The refresh token is written encrypted (<see cref="ITokenProtector"/>) and read with a plain-text fallback, so live rows need
/// no migration and encrypt themselves on their first refresh. The access token stays plain: nine call sites read it directly
/// and it lives two hours.
/// </summary>
public static class ParasutTokenHelper
{
    public const string ReconnectRequiredMessage = "Paraşüt bağlantısı yenilenemedi; Ayarlar → Paraşüt'e Bağlan ile yeniden yetki verin.";
    public const string TemporarilyUnavailableMessage = "Paraşüt'e şu an ulaşılamadı; bağlantı bir sonraki döngüde yeniden denenecek.";

    /// <summary>Returns the connection with a guaranteed-valid access token, or null with an error message.</summary>
    public static async Task<(ParasutConnection? Connection, string? Error)> EnsureValidTokenAsync(
        ParasutConnection? connection,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ITokenProtector tokenProtector,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (connection is null)
            return (null, "Paraşüt bağlantısı bulunamadı. Lütfen Ayarlar'dan Paraşüt'e bağlanın.");

        // A dead new-flow connection is never retried from here: repeated 401s can get the client blocked at Paraşüt.
        if (connection.NeedsReconnect)
            return (null, ReconnectRequiredMessage);

        // 1 ── Token still valid
        if (connection.IsConnected)
            return (connection, null);

        var connTarget = connection.ProjectId.HasValue ? $"project {connection.ProjectId}" : "global";
        var refreshToken = ParasutOAuthFlow.ReadSecret(tokenProtector, connection.RefreshToken);

        // ── NEW FLOW: the refresh token is the only key ──────────────────────────
        if (connection.AuthFlow == ParasutAuthFlow.OAuth2)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                await MarkNeedsReconnectAsync(connection, connectionRepository, "Yenileme belirteci yok.", logger, cancellationToken);
                return (null, ReconnectRequiredMessage);
            }

            try
            {
                logger.LogInformation("Paraşüt belirteci yenileniyor (yetki kodu akışı) — {ConnTarget}.", connTarget);
                var refreshed = await parasutClient.RefreshOAuth2TokenAsync(refreshToken, cancellationToken);
                ApplyToken(connection, refreshed, tokenProtector);
                await connectionRepository.UpdateAsync(connection, cancellationToken);
                return (connection, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ParasutTokenRejectedException ex)
            {
                // Token revoked / expired / the authorising user lost "Uygulama Entegrasyonları" — only a human can fix it.
                logger.LogError(ex, "Paraşüt yenileme reddedildi ({Status}) — yeniden yetkilendirme gerekiyor. {ConnTarget}", ex.StatusCode, connTarget);
                await MarkNeedsReconnectAsync(connection, connectionRepository, ex.Message, logger, cancellationToken);
                return (null, ReconnectRequiredMessage);
            }
            catch (Exception ex)
            {
                // Network / 5xx: not a verdict on the token. Left for the next attempt; the caller may count the failure.
                logger.LogWarning(ex, "Paraşüt yenileme geçici olarak başarısız — {ConnTarget}.", connTarget);
                return (null, TemporarilyUnavailableMessage);
            }
        }

        // ── LEGACY FLOW (until cut-over) ────────────────────────────────────────
        // 2 ── Token expired — try refresh_token grant first
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                logger.LogInformation("Paraşüt access token expired for {ConnTarget}. Attempting refresh...", connTarget);
                var refreshed = await parasutClient.RefreshTokenAsync(refreshToken, connection.ClientId, connection.ClientSecret, cancellationToken);
                ApplyToken(connection, refreshed, tokenProtector);
                await connectionRepository.UpdateAsync(connection, cancellationToken);
                logger.LogInformation("Paraşüt token refreshed for {ConnTarget}. New expiry: {Expiry:u}", connTarget, connection.TokenExpiresAt);
                return (connection, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Paraşüt refresh_token grant failed for {ConnTarget}. Falling back to password grant.", connTarget);
            }
        }

        // 3 ── Refresh failed or no refresh token — full re-authentication with stored credentials
        if (string.IsNullOrEmpty(connection.Username) || string.IsNullOrEmpty(connection.Password))
            return (null, "Paraşüt token süresi dolmuş ve yeniden giriş için kayıtlı bilgiler yok. Lütfen Ayarlar'dan tekrar bağlanın.");

        try
        {
            logger.LogInformation("Paraşüt re-authenticating with stored credentials for {ConnTarget}.", connTarget);
            var newToken = await parasutClient.GetTokenAsync(
                new ParasutTokenRequest(GrantType: "password", ClientId: connection.ClientId, ClientSecret: connection.ClientSecret, Username: connection.Username, Password: connection.Password),
                cancellationToken);
            ApplyToken(connection, newToken, tokenProtector);
            await connectionRepository.UpdateAsync(connection, cancellationToken);
            logger.LogInformation("Paraşüt re-authenticated for {ConnTarget}. New expiry: {Expiry:u}", connTarget, connection.TokenExpiresAt);
            return (connection, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Paraşüt re-authentication failed for {ConnTarget}.", connTarget);
            return (null, "Paraşüt'e otomatik bağlanılamadı. Lütfen Ayarlar'dan şifreyi kontrol edip tekrar bağlanın.");
        }
    }

    /// <summary>
    /// Writes a token response to the row: access token plain, refresh token ENCRYPTED (rotation — always the new one),
    /// expiry of both, and clears the failure counters.
    /// </summary>
    public static void ApplyToken(ParasutConnection connection, ParasutTokenResponse token, ITokenProtector tokenProtector)
    {
        var now = DateTime.UtcNow;
        connection.AccessToken = token.AccessToken;
        connection.RefreshToken = ParasutOAuthFlow.ProtectSecret(tokenProtector, token.RefreshToken);
        connection.TokenExpiresAt = now.AddSeconds(Math.Max(token.ExpiresIn - 60, 60));
        // Keycloak answers refresh_expires_in (30 days); the legacy endpoint does not → keep what we know.
        if (token.RefreshExpiresIn > 0) connection.RefreshTokenExpiresAt = now.AddSeconds(token.RefreshExpiresIn);
        else if (connection.AuthFlow == ParasutAuthFlow.Password) connection.RefreshTokenExpiresAt = null;
        connection.LastConnectedAt = now;
        connection.LastError = null;
        connection.ReconnectAttempts = 0;
        connection.NeedsReconnect = false;
        connection.ReconnectAlertedAt = null;
        connection.UpdatedAt = now;
    }

    /// <summary>Marks a reconnect failure on the connection (increments attempts, stores error message). Does NOT persist.</summary>
    public static void MarkReconnectFailure(ParasutConnection connection, string error)
    {
        connection.ReconnectAttempts++;
        connection.LastError = error.Length > 500 ? error[..500] : error;
        connection.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task MarkNeedsReconnectAsync(ParasutConnection connection, IParasutConnectionRepository repository, string error, ILogger logger, CancellationToken cancellationToken)
    {
        connection.NeedsReconnect = true;
        MarkReconnectFailure(connection, error);
        try { await repository.UpdateAsync(connection, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Paraşüt NeedsReconnect işareti yazılamadı."); }
    }
}
