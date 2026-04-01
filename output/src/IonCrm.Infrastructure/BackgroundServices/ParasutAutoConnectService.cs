using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that keeps all stored Paraşüt connections alive.
///
/// Behaviour:
///   1. On startup — immediately refreshes/re-authenticates all expired tokens (fire-and-forget
///      so Railway health-check is not blocked).
///   2. Periodically (every 30 minutes) — checks all connections and refreshes any that have
///      expired since the last cycle. This prevents the first user API call from triggering a
///      slow re-auth round-trip even if the server has been running for a while.
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
        _logger = logger;
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
                // Skip connections that already have a valid token
                if (connection.IsConnected)
                {
                    _logger.LogDebug(
                        "ParasutAutoConnect: project {ProjectId} token still valid until {Expiry:u} — skipping.",
                        connection.ProjectId, connection.TokenExpiresAt);
                    skipped++;
                    continue;
                }

                // Skip connections that exceeded the max retry limit
                if (connection.ReconnectAttempts >= MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: project {ProjectId} exceeded max reconnect attempts ({Attempts}/{Max}). " +
                        "Last error: {Error}. Manual reconnect required.",
                        connection.ProjectId, connection.ReconnectAttempts, MaxReconnectAttempts, connection.LastError);
                    skipped++;
                    continue;
                }

                // Skip connections that have no credentials stored (cannot re-auth)
                if (string.IsNullOrWhiteSpace(connection.Username) &&
                    string.IsNullOrWhiteSpace(connection.RefreshToken))
                {
                    _logger.LogWarning(
                        "ParasutAutoConnect: project {ProjectId} has no credentials and no refresh token — cannot auto-connect.",
                        connection.ProjectId);
                    failed++;
                    continue;
                }

                _logger.LogInformation(
                    "ParasutAutoConnect: token expired for project {ProjectId} (attempt {Attempt}). Attempting refresh/re-auth...",
                    connection.ProjectId, connection.ReconnectAttempts + 1);

                var (refreshedConn, error) = await ParasutTokenHelper.EnsureValidTokenAsync(
                    connection,
                    parasutClient,
                    connectionRepo,
                    _logger,
                    cancellationToken);

                if (refreshedConn is not null)
                {
                    _logger.LogInformation(
                        "ParasutAutoConnect: project {ProjectId} reconnected. Token valid until {Expiry:u}.",
                        connection.ProjectId, refreshedConn.TokenExpiresAt);
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
                            "ParasutAutoConnect: failed to persist reconnect failure for project {ProjectId}. {Inner}",
                            connection.ProjectId, ex.InnerException?.Message);
                    }

                    _logger.LogWarning(
                        "ParasutAutoConnect: project {ProjectId} auto-connect failed (attempt {Attempt}/{Max}) — {Error}",
                        connection.ProjectId, connection.ReconnectAttempts, MaxReconnectAttempts, error);
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
}
