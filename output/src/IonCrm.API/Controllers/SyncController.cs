using Hangfire;
using IonCrm.API.Common;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Sync.Commands.NotifySaas;
using IonCrm.Application.Features.Sync.Commands.SyncEmsPayments;
using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Application.Features.Sync.Queries.GetSyncLogs;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for SaaS ↔ CRM synchronisation.
///
/// POST /api/v1/sync/saas-a  — SaaS A pushes data here (API-key secured)
/// POST /api/v1/sync/saas-b  — SaaS B pushes data here (API-key secured)
/// GET  /api/v1/sync/logs    — View sync history (SuperAdmin)
/// POST /api/v1/sync/trigger — Manually trigger full sync (SuperAdmin)
/// </summary>
[Route("api/v1/sync")]
public sealed class SyncController : ApiControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initialises a new instance of <see cref="SyncController"/>.</summary>
    public SyncController(IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _scopeFactory  = scopeFactory;
    }

    // ── Inbound webhooks (SaaS pushes to CRM) ─────────────────────────────────

    /// <summary>
    /// Receives a real-time webhook event pushed by SaaS A.
    /// Secured with X-Api-Key header — not JWT.
    /// </summary>
    /// <remarks>
    /// SaaS A must include the header: <c>X-Api-Key: {configured key}</c>.
    /// The payload's ProjectId is derived from the X-Project-Id header.
    /// </remarks>
    [HttpPost("saas-a")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReceiveSaasAWebhook(
        [FromBody] JsonElement rawBody,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromHeader(Name = "X-Project-Id")] string? projectIdHeader,
        CancellationToken cancellationToken)
    {
        // Verify API key
        var expectedKey = _configuration["SaasA:WebhookApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
            return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));

        // Resolve project
        if (!Guid.TryParse(projectIdHeader, out var projectId))
        {
            var defaultProjectId = _configuration["SaasA:ProjectId"];
            if (!Guid.TryParse(defaultProjectId, out projectId))
                return BadRequest(ApiResponse<object>.Fail("Unable to determine ProjectId.", 400));
        }

        var rawJson = rawBody.GetRawText();

        // Extract event metadata from the payload
        var eventType = rawBody.TryGetProperty("eventType", out var evt)
            ? evt.GetString() ?? "unknown"
            : "unknown";
        var entityType = rawBody.TryGetProperty("entityType", out var ent)
            ? ent.GetString() ?? "unknown"
            : "unknown";
        var entityId = rawBody.TryGetProperty("entityId", out var eid)
            ? eid.GetString() ?? ""
            : "";

        var command = new ProcessSaasAWebhookCommand(
            EventType: eventType,
            EntityType: entityType,
            EntityId: entityId,
            ProjectId: projectId,
            RawPayload: rawJson);

        var result = await Mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? OkResponse<object>(new { }, "SaaS A webhook processed.")
            : BadRequest(ApiResponse<object>.Fail(result.Errors));
    }

    /// <summary>
    /// Receives a real-time webhook event pushed by SaaS B.
    /// Secured with X-Api-Key header — not JWT.
    /// </summary>
    /// <remarks>
    /// SaaS B must include the header: <c>X-Api-Key: {configured key}</c>.
    /// The payload's ProjectId is derived from the X-Project-Id header.
    /// </remarks>
    [HttpPost("saas-b")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReceiveSaasBWebhook(
        [FromBody] JsonElement rawBody,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromHeader(Name = "X-Project-Id")] string? projectIdHeader,
        CancellationToken cancellationToken)
    {
        // Verify API key
        var expectedKey = _configuration["SaasB:WebhookApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
            return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));

        // Resolve project
        if (!Guid.TryParse(projectIdHeader, out var projectId))
        {
            var defaultProjectId = _configuration["SaasB:ProjectId"];
            if (!Guid.TryParse(defaultProjectId, out projectId))
                return BadRequest(ApiResponse<object>.Fail("Unable to determine ProjectId.", 400));
        }

        var rawJson = rawBody.GetRawText();

        var eventType = rawBody.TryGetProperty("event", out var evt)
            ? evt.GetString() ?? "unknown"
            : "unknown";
        var entityType = rawBody.TryGetProperty("type", out var ent)
            ? ent.GetString() ?? "unknown"
            : "unknown";
        var entityId = rawBody.TryGetProperty("id", out var eid)
            ? eid.GetString() ?? ""
            : "";

        var command = new ProcessSaasBWebhookCommand(
            Event: eventType,
            Type: entityType,
            Id: entityId,
            ProjectId: projectId,
            RawPayload: rawJson);

        var result = await Mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? OkResponse<object>(new { }, "SaaS B webhook processed.")
            : BadRequest(ApiResponse<object>.Fail(result.Errors));
    }

    // ── SuperAdmin endpoints ───────────────────────────────────────────────────

    /// <summary>
    /// Returns paginated sync log history.
    /// SuperAdmin sees all projects; other roles see only their own project.
    /// </summary>
    [HttpGet("logs")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSyncLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? projectId = null,
        [FromQuery] SyncSource? source = null,
        [FromQuery] SyncDirection? direction = null,
        [FromQuery] SyncStatus? status = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetSyncLogsQuery(page, pageSize, projectId, source, direction, status, fromDate, toDate);
        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Manually triggers a full SaaS sync cycle (fire-and-forget).
    /// Uses Hangfire when enabled; falls back to Task.Run otherwise.
    /// SuperAdmin only.
    /// </summary>
    [HttpPost("trigger")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public IActionResult TriggerSync()
    {
        var hangfireEnabled = _configuration.GetValue<bool>("Hangfire:Enabled", false);

        if (hangfireEnabled)
        {
            // Hangfire serialises this as an expression tree which forbids named/optional args
            // — pass both parameters positionally. 20-min EMS window matches the legacy default.
            var jobId = BackgroundJob.Enqueue<SaasSyncJob>(job => job.RunAsync(20, CancellationToken.None));
            return OkResponse(new { JobId = jobId, Mode = "hangfire" },
                "Sync job enqueued via Hangfire.");
        }

        // Hangfire disabled — run directly in a background thread
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
                var job    = scope.ServiceProvider.GetRequiredService<SaasSyncJob>();
                logger.LogInformation("Manual sync trigger: starting background job.");
                await job.RunAsync(cancellationToken: CancellationToken.None);
                logger.LogInformation("Manual sync trigger: background job completed.");
            }
            catch (Exception ex)
            {
                // Log via a fresh scope since the original scope may be disposed
                using var errScope = _scopeFactory.CreateScope();
                var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
                logger.LogError(ex, "Manual sync trigger: background job failed with unhandled exception.");
            }
        });

        return OkResponse(new { JobId = (string?)null, Mode = "direct" },
            "Sync job started in background.");
    }

    /// <summary>
    /// Fetches recent completed payments from all EMS-connected projects and
    /// auto-creates invoice drafts for any payment not yet recorded.
    /// SuperAdmin only.
    /// </summary>
    /// <param name="windowMinutes">How many minutes back to look for payments (default: 20).</param>
    [HttpPost("ems-payments")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<SyncEmsPaymentsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SyncEmsPayments(
        [FromQuery] int windowMinutes = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new SyncEmsPaymentsCommand(windowMinutes), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// One-shot destructive reset: hard-deletes every <c>Customer</c> row whose <c>LegacyId</c>
    /// starts with <c>LIFT-</c> so the next Liftdesk sync repopulates the mirror from scratch
    /// against the new API host.  Cascades to every dependent row via the FK constraints —
    /// <b>ContactHistories, CustomerTasks, Opportunities, Invoices and CustomerContracts</b>
    /// attached to those customers ARE ALSO removed.  Meant for the Liftdesk host cutover;
    /// requires <c>confirm=true</c> to fire so an idle click cannot wipe production data.
    ///
    /// After the delete, kicks off <see cref="SaasSyncJob"/> in the background so the mirror
    /// starts refilling immediately (same fire-and-forget path as <see cref="TriggerSync"/>).
    /// SuperAdmin only.
    /// </summary>
    [HttpPost("reset-liftdesk")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetLiftdesk(
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        // Guard against accidental invocation. The frontend/UI never exposes this — it's a
        // deliberate action reached via the API client / Swagger with an explicit flag.
        if (!confirm)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Reset işlemi kalıcıdır ve tüm Liftdesk müşterilerini (LIFT-*) ve bağlı kayıtlarını " +
                "(iletişim geçmişi, görevler, fırsatlar, faturalar, sözleşmeler) siler. " +
                "Onaylamak için ?confirm=true parametresiyle tekrar çağırın.",
                400));
        }

        int deleted;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();

            // Hard delete — soft-deleted rows would block the sync from re-inserting because
            // the upsert path skips existing IsDeleted rows to respect user deletions.
            deleted = await db.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ""Customers"" WHERE ""LegacyId"" LIKE 'LIFT-%'",
                cancellationToken);

            logger.LogWarning(
                "Reset Liftdesk: hard-deleted {Count} Customer row(s) with LegacyId LIKE 'LIFT-%'. " +
                "Dependent ContactHistories / Tasks / Opportunities / Invoices / Contracts cascaded.",
                deleted);
        }

        // Kick off a fresh sync so the mirror starts filling straight away.
        _ = Task.Run(async () =>
        {
            try
            {
                using var syncScope = _scopeFactory.CreateScope();
                var logger = syncScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
                var job    = syncScope.ServiceProvider.GetRequiredService<SaasSyncJob>();
                logger.LogInformation("Reset Liftdesk: triggering fresh SaaS sync.");
                await job.RunAsync(cancellationToken: CancellationToken.None);
                logger.LogInformation("Reset Liftdesk: fresh sync completed.");
            }
            catch (Exception ex)
            {
                using var errScope = _scopeFactory.CreateScope();
                var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
                logger.LogError(ex, "Reset Liftdesk: post-reset sync failed with unhandled exception.");
            }
        });

        return OkResponse(
            new { DeletedCustomers = deleted, SyncTriggered = true },
            $"{deleted} Liftdesk müşteri kaydı silindi. Yeni URL üzerinden sync arka planda başlatıldı.");
    }
}
