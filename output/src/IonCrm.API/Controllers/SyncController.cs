using Hangfire;
using IonCrm.API.Common;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Sync.Commands.SyncEmsPayments;
using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Application.Features.Sync.Queries.GetSyncLogs;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for SaaS ↔ CRM synchronisation.
///
/// POST /api/v1/sync/saas-b  — SaaS B (RezervAl) pushes data here (API-key secured)
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
    /// One-shot reset: hard-deletes <c>LIFT-*</c> Customer rows that own NO child data, so the
    /// next Liftdesk sync repopulates the mirror from scratch against the new API host.
    ///
    /// Customers with ANY dependent rows (contact histories, tasks, opportunities, invoices,
    /// contracts) are PRESERVED — a hard delete would cascade to those children, and after the
    /// EMS→Liftdesk data migration the LIFT rows carry the retired EMS platform's only surviving
    /// CRM archive, which must never be destroyed by a mirror reset. Preserved rows are simply
    /// updated in place by the next sync.  Requires <c>confirm=true</c>.
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
                "Reset işlemi çocuk kaydı olmayan Liftdesk müşterilerini (LIFT-*) kalıcı olarak siler; " +
                "iletişim geçmişi / görev / fırsat / fatura / sözleşme taşıyan müşteriler korunur. " +
                "Onaylamak için ?confirm=true parametresiyle tekrar çağırın.",
                400));
        }

        int deleted;
        int preserved;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();

            // Hard delete of CHILDLESS rows only — soft-deleted rows would block the sync from
            // re-inserting (the upsert path skips existing IsDeleted rows), but rows that own
            // children must survive: the FK cascade would otherwise destroy the migrated EMS
            // archive (and any CRM work) irrecoverably.
            deleted = await db.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ""Customers"" AS c
                  WHERE c.""LegacyId"" LIKE 'LIFT-%'
                    AND NOT EXISTS (SELECT 1 FROM ""ContactHistories""  t WHERE t.""CustomerId"" = c.""Id"")
                    AND NOT EXISTS (SELECT 1 FROM ""CustomerTasks""     t WHERE t.""CustomerId"" = c.""Id"")
                    AND NOT EXISTS (SELECT 1 FROM ""Opportunities""     t WHERE t.""CustomerId"" = c.""Id"")
                    AND NOT EXISTS (SELECT 1 FROM ""Invoices""          t WHERE t.""CustomerId"" = c.""Id"")
                    AND NOT EXISTS (SELECT 1 FROM ""CustomerContracts"" t WHERE t.""CustomerId"" = c.""Id"")",
                cancellationToken);

            preserved = await db.Customers
                .IgnoreQueryFilters()
                .CountAsync(c => c.LegacyId != null && c.LegacyId.StartsWith("LIFT-"), cancellationToken);

            logger.LogWarning(
                "Reset Liftdesk: hard-deleted {Deleted} childless LIFT-* Customer row(s); " +
                "{Preserved} row(s) with dependent data preserved.",
                deleted, preserved);
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
            new { DeletedCustomers = deleted, PreservedCustomers = preserved, SyncTriggered = true },
            $"{deleted} çocuksuz Liftdesk müşteri kaydı silindi, {preserved} veri taşıyan kayıt korundu. " +
            "Sync arka planda başlatıldı.");
    }

    /// <summary>
    /// One-shot migration of the retired EMS platform's CRM data onto the Liftdesk successor
    /// customers.  Matching is NAME-BASED (company ids were NOT preserved by the EMS→Liftdesk
    /// platform migration): an EMS customer ("3" / "SAASA-3" / "EMS-3") maps to the single LIFT-*
    /// customer with the same normalized company name, falling back to a unique "core name" match
    /// (generic tokens like asansör/ltd/şti stripped). Ambiguous names are reported, never guessed.
    ///
    /// Per matched pair: every child row (contact histories, tasks, opportunities, invoices,
    /// contracts) is re-pointed to the Liftdesk customer (denormalized ProjectId rewritten too);
    /// CRM-only fields (Label, Code, AssignedUserId, ParasutContactId, e-invoice flags, contact
    /// name) are copied where the target is empty; a CRM-side soft-delete on the EMS row carries
    /// over to the Liftdesk row; the EMS row is retired (soft-delete + LegacyId → EMSMIGRATED-{id},
    /// making re-runs idempotent).  EMS customers without a unique name match are reported and left
    /// untouched.  Executes inside a single DB transaction — any failure rolls everything back.
    ///
    /// Call with ?dryRun=true (default) first: zero writes, full matching report. Then execute
    /// with ?dryRun=false&amp;confirm=true.  SuperAdmin only.
    /// </summary>
    [HttpPost("migrate-ems-to-liftdesk")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MigrateEmsToLiftdesk(
        [FromQuery] bool dryRun = true,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun && !confirm)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Bu işlem EMS müşterilerinin tüm verilerini (iletişim geçmişi, görevler, fırsatlar, " +
                "faturalar, sözleşmeler) Liftdesk karşılıklarına taşır ve EMS kayıtlarını emekliye ayırır. " +
                "Önce ?dryRun=true ile raporu inceleyin; uygulamak için ?dryRun=false&confirm=true ile çağırın.",
                400));
        }

        using var scope = _scopeFactory.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<EmsToLiftdeskMigrationService>();

        try
        {
            var report = await migrator.MigrateAsync(dryRun, cancellationToken);
            var message = dryRun
                ? $"DRY-RUN: {report.MigratedPairs} eşleşme, {report.UnmatchedCount} eşleşmeyen. Hiçbir veri değişmedi."
                : $"{report.MigratedPairs} EMS müşterisi Liftdesk karşılığına taşındı ({report.UnmatchedCount} eşleşmeyen dokunulmadı).";
            return OkResponse<object>(report, message);
        }
        catch (Exception ex)
        {
            using var errScope = _scopeFactory.CreateScope();
            var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
            logger.LogError(ex, "EMS→Liftdesk migration failed (dryRun={DryRun}). Transaction rolled back.", dryRun);
            return BadRequest(ApiResponse<object>.Fail(
                $"Migration başarısız (tüm değişiklikler geri alındı): {ex.GetBaseException().Message}", 400));
        }
    }

    /// <summary>
    /// One-shot move of the EMS project's LEAD records (LegacyId null or "PC-*") into the
    /// Liftdesk project — the customer rows themselves (labels, statuses, contact info intact)
    /// plus every child row (contact histories, tasks, opportunities, invoices, contracts) via
    /// a denormalized-ProjectId rewrite. Soft-deleted leads move too. No merging: leads whose
    /// name matches an existing live customer in the target are flagged in the report only.
    /// Call with ?dryRun=true (default) first; execute with ?dryRun=false&amp;confirm=true.
    /// SuperAdmin only.
    /// </summary>
    [HttpPost("move-ems-leads")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MoveEmsLeads(
        [FromQuery] Guid sourceProjectId,
        [FromQuery] Guid targetProjectId,
        [FromQuery] bool dryRun = true,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (sourceProjectId == Guid.Empty || targetProjectId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail(
                "sourceProjectId ve targetProjectId zorunludur.", 400));

        if (!dryRun && !confirm)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Bu işlem kaynak projedeki tüm lead kayıtlarını (alt kayıtlarıyla birlikte) hedef projeye " +
                "taşır. Önce ?dryRun=true ile raporu inceleyin; uygulamak için ?dryRun=false&confirm=true ile çağırın.",
                400));
        }

        using var scope = _scopeFactory.CreateScope();
        var mover = scope.ServiceProvider.GetRequiredService<EmsLeadMoveService>();

        try
        {
            var report = await mover.MoveAsync(sourceProjectId, targetProjectId, dryRun, cancellationToken);
            var message = dryRun
                ? $"DRY-RUN: {report.LeadsFound} lead taşınacak. Hiçbir veri değişmedi."
                : $"{report.LeadsMoved} lead hedef projeye taşındı.";
            return OkResponse<object>(report, message);
        }
        catch (Exception ex)
        {
            using var errScope = _scopeFactory.CreateScope();
            var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
            logger.LogError(ex, "EMS lead move failed (dryRun={DryRun}). Transaction rolled back.", dryRun);
            return BadRequest(ApiResponse<object>.Fail(
                $"Lead taşıma başarısız (tüm değişiklikler geri alındı): {ex.GetBaseException().Message}", 400));
        }
    }

    /// <summary>
    /// Deletes childless ZOMBIE rows the EMS sync re-inserted after the migration: live
    /// bare-numeric rows whose project also holds the matching EMSMIGRATED-{id} marker.
    /// Genuine un-migrated customers (no marker) are never touched; zombies that somehow own
    /// children are kept and reported. Call with ?dryRun=true (default) first; execute with
    /// ?dryRun=false&amp;confirm=true. SuperAdmin only.
    /// </summary>
    [HttpPost("purge-ems-zombies")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PurgeEmsZombies(
        [FromQuery] bool dryRun = true,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun && !confirm)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Bu işlem EMS sync'inin migration sonrası yeniden eklediği çocuksuz zombi kayıtları " +
                "kalıcı olarak siler. Önce ?dryRun=true ile raporu inceleyin; uygulamak için " +
                "?dryRun=false&confirm=true ile çağırın.", 400));
        }

        using var scope = _scopeFactory.CreateScope();
        var purger = scope.ServiceProvider.GetRequiredService<EmsZombiePurgeService>();

        try
        {
            var report = await purger.PurgeAsync(dryRun, cancellationToken);
            var message = dryRun
                ? $"DRY-RUN: {report.ZombiesFound} zombi bulundu, {report.ZombiesDeleted} silinecek. Hiçbir veri değişmedi."
                : $"{report.ZombiesDeleted} zombi kayıt silindi ({report.KeptWithChildren} çocuklu kayıt korundu).";
            return OkResponse<object>(report, message);
        }
        catch (Exception ex)
        {
            using var errScope = _scopeFactory.CreateScope();
            var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
            logger.LogError(ex, "EMS zombie purge failed (dryRun={DryRun}). Transaction rolled back.", dryRun);
            return BadRequest(ApiResponse<object>.Fail(
                $"Zombi temizliği başarısız (tüm değişiklikler geri alındı): {ex.GetBaseException().Message}", 400));
        }
    }

    /// <summary>
    /// Undoes an executed EMS→Liftdesk migration using the plan derived from its reports.
    /// Moves every child back from the LIFT targets to the restored EMS sources, clears the
    /// copied fields, un-deletes carry-over-deleted targets, restores the EMSMIGRATED rows to
    /// their original LegacyId + deletion state, and deletes childless zombie duplicates the
    /// still-running EMS sync re-inserted in the meantime.  Body: the RollbackPlan JSON.
    /// Call with ?dryRun=true first; execute with ?dryRun=false&amp;confirm=true.  SuperAdmin only.
    /// </summary>
    [HttpPost("rollback-ems-migration")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RollbackEmsMigration(
        [FromBody] RollbackPlan plan,
        [FromQuery] bool dryRun = true,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (plan?.Pairs is not { Count: > 0 })
            return BadRequest(ApiResponse<object>.Fail("Rollback planı boş — body'de Pairs listesi gerekli.", 400));

        if (!dryRun && !confirm)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Bu işlem migration'ı geri alır: çocuk kayıtlar LIFT hedeflerinden EMS kaynaklarına geri taşınır, " +
                "kopyalanan alanlar temizlenir, zombi kayıtlar silinir. Önce ?dryRun=true ile raporu inceleyin; " +
                "uygulamak için ?dryRun=false&confirm=true ile çağırın.",
                400));
        }

        using var scope = _scopeFactory.CreateScope();
        var rollback = scope.ServiceProvider.GetRequiredService<EmsMigrationRollbackService>();

        try
        {
            var report = await rollback.RollbackAsync(plan, dryRun, cancellationToken);
            var message = dryRun
                ? $"DRY-RUN: {report.GroupsProcessed} grup geri alınacak, {report.GroupsSkipped} atlanacak. Hiçbir veri değişmedi."
                : $"{report.GroupsProcessed} grup geri alındı: {report.ChildrenMovedBack} çocuk kayıt, {report.SourcesRestored} kaynak, {report.ZombiesDeleted} zombi silindi.";
            return OkResponse<object>(report, message);
        }
        catch (Exception ex)
        {
            using var errScope = _scopeFactory.CreateScope();
            var logger = errScope.ServiceProvider.GetRequiredService<ILogger<SyncController>>();
            logger.LogError(ex, "EMS migration rollback failed (dryRun={DryRun}). Transaction rolled back.", dryRun);
            return BadRequest(ApiResponse<object>.Fail(
                $"Rollback başarısız (tüm değişiklikler geri alındı): {ex.GetBaseException().Message}", 400));
        }
    }
}
