using Hangfire;
using IonCrm.API.Common;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Sync.Commands.NotifySaas;
using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Application.Features.Sync.Queries.GetSyncLogs;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.BackgroundServices;
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
            var jobId = BackgroundJob.Enqueue<SaasSyncJob>(job => job.RunAsync(CancellationToken.None));
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
                await job.RunAsync(CancellationToken.None);
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
}
