using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that keeps all stored Paraşüt connections alive AND ensures
/// the connection is project-independent (global) so all projects share the same
/// Paraşüt company.
///
/// Behaviour:
///   1. On startup — promotes any pre-existing project-specific connection to global
///      (sets <c>ProjectId = null</c>) when no global connection exists yet, then
///      refreshes/re-authenticates all expired tokens (fire-and-forget so the Railway
///      health-check is not blocked).
///   2. Periodically (every 30 minutes) — checks all connections and refreshes any
///      that have expired since the last cycle. On the new flow this also keeps the
///      30-day refresh-token window rolling: every refresh resets it.
///
/// Promotion rules (one-time, idempotent):
///   • Global connection exists           → no-op
///   • No global, no project-specific     → no-op (manual setup mode)
///   • No global, project-specific exists → take the most recently updated one and
///                                           set its ProjectId to null (promote to global).
///
/// Per expired <c>ParasutConnection</c> the service asks <see cref="ParasutTokenHelper"/>:
///   • legacy rows: refresh_token grant, then password grant with the stored credentials;
///   • new-flow rows: refresh_token grant only — there is no password. A rejected refresh
///     marks the row <c>NeedsReconnect</c> and it is never retried here.
///
/// Silence is not success: when a connection is dead (new flow rejected, or a legacy row
/// hit the retry limit) ONE alert mail goes out through <see cref="INotificationEmailSender"/>
/// (ops recipients) and <c>ReconnectAlertedAt</c> is stamped so it is not repeated. A
/// successful connection clears the stamp.
///
/// IMPORTANT: Uses <c>IParasutConnectionRepository.GetAllAsync()</c> which calls
/// <c>IgnoreQueryFilters()</c> — no HTTP context is available, so the EF tenant filter
/// would block all rows without this.
/// </summary>
public sealed class ParasutAutoConnectService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParasutAutoConnectService> _logger;

    /// <summary>Interval between periodic reconnect checks.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum consecutive failures before we stop retrying a specific legacy connection.
    /// After this limit, the user must manually reconnect from the UI — and is told so by mail.
    /// </summary>
    private const int MaxReconnectAttempts = 10;

    /// <summary>Initialises a new instance of <see cref="ParasutAutoConnectService"/>.</summary>
    public ParasutAutoConnectService(
        IServiceScopeFactory scopeFactory,
        ILogger<ParasutAutoConnectService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fire first check immediately on startup (non-blocking so health-check passes).
        await RefreshAllConnectionsAsync(stoppingToken);

        // Periodic loop
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAllConnectionsAsync(stoppingToken);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task RefreshAllConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var connectionRepo = scope.ServiceProvider.GetRequiredService<IParasutConnectionRepository>();
            var parasutClient  = scope.ServiceProvider.GetRequiredService<IParasutClient>();
            var tokenProtector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();
            var mail           = scope.ServiceProvider.GetRequiredService<INotificationEmailSender>();
            var configuration  = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // Promote any pre-existing project-specific connection to global BEFORE the
            // refresh loop, so the freshly-promoted connection is picked up by the
            // GetAllAsync() call below.
            await EnsureGlobalConnectionExistsAsync(connectionRepo, cancellationToken);

            // GetAllAsync() uses IgnoreQueryFilters() — required for background services.
            var connections = await connectionRepo.GetAllAsync(cancellationToken);

            if (connections.Count == 0)
            {
                _logger.LogDebug("ParasutAutoConnect: no stored connections found.");
                return;
            }

            _logger.LogInformation(
                "ParasutAutoConnect: checking {Count} stored Paraşüt connection(s).",
                connections.Count);

            int refreshed = 0;
            int skipped   = 0;
            int failed    = 0;

            foreach (var connection in connections)
            {
                var connTarget = connection.ProjectId.HasValue
                    ? $"project {connection.ProjectId}"
                    : "global";

                // Skip connections that already have a valid token
                if (connection.IsConnected)
                {
                    _logger.LogDebug(
                        "ParasutAutoConnect: {ConnTarget} token still valid until {Expiry:u} — skipping.",
                        connTarget, connection.TokenExpiresAt);
                    skipped++;
                    continue;
                }

                // Dead new-flow connection: never retried (repeated 401s can get the client blocked) — but never silent either.
                if (connection.NeedsReconnect)
                {
                    await AlertOnceAsync(connection, connectionRepo, mail, configuration, cancellationToken);
                    skipped++;
                    continue;
                }

                // New-flow row whose authorisation was started but never completed (or a cleared row): nothing to refresh yet.
                if (connection.AuthFlow == ParasutAuthFlow.OAuth2 && string.IsNullOrWhiteSpace(connection.RefreshToken))
                {
                    _logger.LogDebug("ParasutAutoConnect: {ConnTarget} awaits authorisation (no refresh token yet) — skipping.", connTarget);
                    skipped++;
                    continue;
                }

                // Skip connections that exceeded the max retry limit — and make sure somebody knows.
                if (connection.ReconnectAttempts >= MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: {ConnTarget} exceeded max reconnect attempts ({Attempts}/{Max}). " +
                        "Last error: {Error}. Manual reconnect required.",
                        connTarget, connection.ReconnectAttempts, MaxReconnectAttempts, connection.LastError);
                    await AlertOnceAsync(connection, connectionRepo, mail, configuration, cancellationToken);
                    skipped++;
                    continue;
                }

                // Legacy rows that have no credentials stored (cannot re-auth)
                if (connection.AuthFlow == ParasutAuthFlow.Password &&
                    string.IsNullOrWhiteSpace(connection.Username) &&
                    string.IsNullOrWhiteSpace(connection.RefreshToken))
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: {ConnTarget} has no credentials and no refresh token — cannot auto-connect.",
                        connTarget);
                    failed++;
                    continue;
                }

                _logger.LogInformation(
                    "ParasutAutoConnect: token expired for {ConnTarget} (attempt {Attempt}, flow {Flow}). Attempting refresh...",
                    connTarget, connection.ReconnectAttempts + 1, connection.AuthFlow);

                var (refreshedConn, error) = await ParasutTokenHelper.EnsureValidTokenAsync(
                    connection,
                    parasutClient,
                    connectionRepo,
                    tokenProtector,
                    _logger,
                    cancellationToken);

                if (refreshedConn is not null)
                {
                    _logger.LogInformation(
                        "ParasutAutoConnect: {ConnTarget} reconnected. Token valid until {Expiry:u}; refresh token until {RefreshExpiry:u}.",
                        connTarget, refreshedConn.TokenExpiresAt, refreshedConn.RefreshTokenExpiresAt);
                    refreshed++;
                    continue;
                }

                // The helper already persisted a NeedsReconnect verdict; transient failures are counted here.
                if (!connection.NeedsReconnect)
                {
                    ParasutTokenHelper.MarkReconnectFailure(connection, error ?? "Unknown error");
                    try
                    {
                        await connectionRepo.UpdateAsync(connection, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "ParasutAutoConnect: failed to persist reconnect failure for {ConnTarget}. {Inner}",
                            connTarget, ex.InnerException?.Message);
                    }
                }

                _logger.LogWarning(
                    "ParasutAutoConnect: {ConnTarget} auto-connect failed (attempt {Attempt}/{Max}) — {Error}",
                    connTarget, connection.ReconnectAttempts, MaxReconnectAttempts, error);
                failed++;

                if (connection.NeedsReconnect || connection.ReconnectAttempts >= MaxReconnectAttempts)
                    await AlertOnceAsync(connection, connectionRepo, mail, configuration, cancellationToken);
            }

            _logger.LogInformation(
                "ParasutAutoConnect complete. Refreshed={Refreshed} Skipped={Skipped} Failed={Failed}",
                refreshed, skipped, failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown — do not log as error.
            _logger.LogInformation("ParasutAutoConnect: shutting down gracefully.");
        }
        catch (Exception ex)
        {
            // Never crash the service — log and continue to next cycle.
            _logger.LogError(ex,
                "ParasutAutoConnect: unexpected error during token refresh cycle.");
        }
    }

    /// <summary>
    /// "The integration is dead and needs a human" — said once per outage, by mail to the ops recipients
    /// (<c>Notifications:Recipients</c>). The stamp is cleared when the connection comes back, so the next outage alerts again.
    /// Without a configured sender the fact is logged as an error and stamped anyway, so a half-hourly loop does not spam.
    /// </summary>
    private async Task AlertOnceAsync(
        ParasutConnection connection,
        IParasutConnectionRepository connectionRepo,
        INotificationEmailSender mail,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (connection.ReconnectAlertedAt is not null) return;

        var reason = connection.NeedsReconnect
            ? "Paraşüt yenileme isteğini reddetti (yetki kodu akışı). Belirteç düşmüş, firma Paraşüt'te erişimi durdurmuş ya da yetkilendiren kullanıcının \"Uygulama Entegrasyonları\" yetkisi kaldırılmış olabilir."
            : $"{connection.ReconnectAttempts} ardışık yenileme denemesi başarısız oldu; otomatik deneme durdu.";
        var settings = AppUrls.FrontendBaseUrl(key => configuration[key]) is { } frontend ? $"{frontend}/settings" : null;
        var subject = "Paraşüt bağlantısı koptu — yeniden bağlanma gerekiyor";
        var html =
            $"<p><b>ION CRM'in Paraşüt bağlantısı yenilenemiyor.</b> Cari eşitleme, fatura aktarımı ve tahsilat yazımı bu düzeltilene kadar çalışmaz.</p>" +
            $"<p>{System.Net.WebUtility.HtmlEncode(reason)}</p>" +
            (string.IsNullOrWhiteSpace(connection.LastError) ? string.Empty : $"<p>Son hata: <code>{System.Net.WebUtility.HtmlEncode(connection.LastError)}</code></p>") +
            $"<p>Firma #{connection.CompanyId}{(string.IsNullOrWhiteSpace(connection.CompanyName) ? string.Empty : " · " + System.Net.WebUtility.HtmlEncode(connection.CompanyName))} · akış: {connection.AuthFlow}</p>" +
            (settings is null
                ? "<p>Ayarlar → Paraşüt Entegrasyonu → <b>Paraşüt'e Bağlan</b> ile yeniden yetki verin.</p>"
                : $"<p><a href=\"{settings}\">Ayarlar → Paraşüt Entegrasyonu</a> → <b>Paraşüt'e Bağlan</b> ile yeniden yetki verin.</p>");

        var sent = mail.IsConfigured && await mail.SendAsync(subject, html, cancellationToken);
        if (!sent)
            _logger.LogError("ParasutAutoConnect: bağlantı öldü ve bildirim gönderilemedi (Notifications__Smtp__*/Recipients tanımlı mı?). {Reason}", reason);

        connection.ReconnectAlertedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;
        try { await connectionRepo.UpdateAsync(connection, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "ParasutAutoConnect: ReconnectAlertedAt yazılamadı."); }
    }

    /// <summary>
    /// Ensures a project-independent (global, <c>ProjectId = null</c>) Paraşüt connection
    /// exists.  If no global connection is found but at least one project-specific
    /// connection exists, the most recently updated project-specific connection is
    /// promoted to global by setting its <c>ProjectId</c> to <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This is a one-time auto-migration for users who configured Paraşüt via the Settings
    /// UI before the "global connection" feature shipped — their connection was saved with
    /// the current project's id but the new Settings UI only queries the global connection,
    /// so the existing record was effectively orphaned.  Idempotent on subsequent runs.
    /// </remarks>
    private async Task EnsureGlobalConnectionExistsAsync(
        IParasutConnectionRepository connectionRepo,
        CancellationToken cancellationToken)
    {
        var existingGlobal = await connectionRepo.GetGlobalAsync(cancellationToken);
        if (existingGlobal is not null)
        {
            // Already global — nothing to do
            return;
        }

        // No global yet — look for project-specific connections to promote
        var allConnections = await connectionRepo.GetAllAsync(cancellationToken);
        var projectSpecific = allConnections
            .Where(c => c.ProjectId.HasValue)
            .OrderByDescending(c => c.UpdatedAt)
            .ToList();

        if (projectSpecific.Count == 0)
        {
            _logger.LogDebug(
                "ParasutAutoConnect: no global or project-specific connection found — manual setup required.");
            return;
        }

        var promote = projectSpecific[0];
        var oldProjectId = promote.ProjectId;
        promote.ProjectId = null;

        try
        {
            await connectionRepo.UpdateAsync(promote, cancellationToken);

            _logger.LogInformation(
                "ParasutAutoConnect: promoted project-specific Paraşüt connection (was project {OldProjectId}) " +
                "to global. CompanyId={CompanyId} Username={Username}. " +
                "All projects now share this connection.",
                oldProjectId, promote.CompanyId, promote.Username);

            if (projectSpecific.Count > 1)
            {
                _logger.LogWarning(
                    "ParasutAutoConnect: {Count} additional project-specific connections still exist — " +
                    "they remain project-bound. Promoted the most recently updated one.",
                    projectSpecific.Count - 1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ParasutAutoConnect: failed to promote project-specific connection {Id} to global. {Inner}",
                promote.Id, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
