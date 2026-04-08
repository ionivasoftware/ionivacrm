using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Interfaces;
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
///      that have expired since the last cycle. This prevents the first user API call
///      from triggering a slow re-auth round-trip.
///
/// Promotion rules (one-time, idempotent):
///   • Global connection exists           → no-op
///   • No global, no project-specific     → no-op (manual setup mode)
///   • No global, project-specific exists → take the most recently updated one and
///                                           set its ProjectId to null (promote to global).
///                                           This handles the migration of users who
///                                           configured Paraşüt before the global-connection
///                                           feature shipped.
///
/// For each <c>ParasutConnection</c> with an expired token, the service attempts:
///   1. Silent refresh via the stored refresh_token (if present)
///   2. Full re-authentication via the stored username + password
///
/// Failure tracking: on each failed attempt, <c>ReconnectAttempts</c> is incremented and
/// <c>LastError</c> is stored on the entity. On success, both are cleared and
/// <c>LastConnectedAt</c> is set.
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
    /// Maximum consecutive failures before we stop retrying a specific connection.
    /// After this limit, the user must manually reconnect from the UI.
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
            var connectionRepo  = scope.ServiceProvider.GetRequiredService<IParasutConnectionRepository>();
            var parasutClient   = scope.ServiceProvider.GetRequiredService<IParasutClient>();

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

                // Skip connections that exceeded the max retry limit
                if (connection.ReconnectAttempts >= MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: {ConnTarget} exceeded max reconnect attempts ({Attempts}/{Max}). " +
                        "Last error: {Error}. Manual reconnect required.",
                        connTarget, connection.ReconnectAttempts, MaxReconnectAttempts, connection.LastError);
                    skipped++;
                    continue;
                }

                // Skip connections that have no credentials stored (cannot re-auth)
                if (string.IsNullOrWhiteSpace(connection.Username) &&
                    string.IsNullOrWhiteSpace(connection.RefreshToken))
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: {ConnTarget} has no credentials and no refresh token — cannot auto-connect.",
                        connTarget);
                    failed++;
                    continue;
                }

                _logger.LogInformation(
                    "ParasutAutoConnect: token expired for {ConnTarget} (attempt {Attempt}). Attempting refresh/re-auth...",
                    connTarget, connection.ReconnectAttempts + 1);

                var (refreshedConn, error) = await ParasutTokenHelper.EnsureValidTokenAsync(
                    connection,
                    parasutClient,
                    connectionRepo,
                    _logger,
                    cancellationToken);

                if (refreshedConn is not null)
                {
                    _logger.LogInformation(
                        "ParasutAutoConnect: {ConnTarget} reconnected. Token valid until {Expiry:u}.",
                        connTarget, refreshedConn.TokenExpiresAt);
                    refreshed++;
                }
                else
                {
                    // Track failure on the entity and persist
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

                    _logger.LogWarning(
                        "ParasutAutoConnect: {ConnTarget} auto-connect failed (attempt {Attempt}/{Max}) — {Error}",
                        connTarget, connection.ReconnectAttempts, MaxReconnectAttempts, error);
                    failed++;
                }
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
