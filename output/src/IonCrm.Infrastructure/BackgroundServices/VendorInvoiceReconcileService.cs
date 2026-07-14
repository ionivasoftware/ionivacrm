using IonCrm.Application.Features.VendorInvoices;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IonCrm.Infrastructure.BackgroundServices;

/// <summary>
/// Timer-based service that runs the vendor-invoice reconcile sweep once a day.
/// Reconcile is idempotent and cheap, so a daily cadence reliably catches invoices that pass their
/// due date without needing a precise monthly cron. Overdue Expected records flip to Missing and are
/// surfaced as the CRM's red alarm badge.
///
/// A PostgreSQL advisory lock guarantees only one replica runs the sweep during a rolling deploy.
/// </summary>
public sealed class VendorInvoiceReconcileService : BackgroundService
{
    /// <summary>How often the reconcile sweep fires.</summary>
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromHours(24);

    /// <summary>Delay before the first sweep so EF migrations / bootstrap SQL can finish.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(90);

    /// <summary>Advisory lock key — must not collide with any other lock in the app (cf. SyncTimerService).</summary>
    private const long AdvisoryLockKey = 5_281_744_910_233_617L;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VendorInvoiceReconcileService> _logger;

    /// <summary>Initialises a new instance of <see cref="VendorInvoiceReconcileService"/>.</summary>
    public VendorInvoiceReconcileService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<VendorInvoiceReconcileService> logger)
    {
        _scopeFactory  = scopeFactory;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VendorInvoiceReconcileService started — running every {Hours}h.", ReconcileInterval.TotalHours);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        await RunWithLockAsync(stoppingToken);

        using var timer = new PeriodicTimer(ReconcileInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunWithLockAsync(stoppingToken);
    }

    private async Task RunWithLockAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("VendorInvoiceReconcileService: DefaultConnection missing — skipping.");
            return;
        }

        await using var lockConn = new NpgsqlConnection(connectionString);
        try { await lockConn.OpenAsync(ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "VendorInvoiceReconcileService: could not open lock connection — skipping.");
            return;
        }

        bool lockAcquired;
        try
        {
            await using var lockCmd = new NpgsqlCommand($"SELECT pg_try_advisory_lock({AdvisoryLockKey})", lockConn);
            lockAcquired = (bool)(await lockCmd.ExecuteScalarAsync(ct))!;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "VendorInvoiceReconcileService: advisory lock query failed — skipping.");
            return;
        }

        if (!lockAcquired) return; // another replica is running the sweep

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            // Phase 2: refresh expected amounts from cost APIs before reconciling. Current month keeps
            // the running total fresh; previous month finalises it before its due date passes. Expect is
            // idempotent and never downgrades a Received/Reconciled row, so a daily run is safe.
            try
            {
                var autoExpect = scope.ServiceProvider.GetService<ICostAutoExpectService>();
                if (autoExpect is not null)
                {
                    var now = DateTime.UtcNow;
                    var prev = now.AddMonths(-1);
                    await autoExpect.RunAsync(now.Year, now.Month, ct);
                    await autoExpect.RunAsync(prev.Year, prev.Month, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VendorInvoiceReconcileService: auto-expect step failed (continuing to reconcile).");
            }

            var service = scope.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var result  = await service.ReconcileAsync(asOf: null, cancellationToken: ct);
            if (result.IsSuccess && result.Value is { MissingCount: > 0 } r)
                _logger.LogWarning("VendorInvoiceReconcileService: {Count} eksik fatura.", r.MissingCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VendorInvoiceReconcileService: reconcile cycle failed.");
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLockKey})", lockConn);
                await unlockCmd.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VendorInvoiceReconcileService: explicit advisory unlock failed (released on connection close).");
            }
        }
    }
}
