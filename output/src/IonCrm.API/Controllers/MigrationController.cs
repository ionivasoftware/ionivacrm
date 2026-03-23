using IonCrm.API.Common;
using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Migration.Commands.RunMigration;
using IonCrm.Application.Migration.Queries.GetMigrationStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for the one-time data migration from the legacy MSSQL CRM database.
/// All endpoints require the <c>SuperAdmin</c> policy.
///
/// POST /api/v1/migration/run    — starts the migration job (fire-and-forget)
/// GET  /api/v1/migration/status — returns current progress snapshot
/// </summary>
[Route("api/v1/migration")]
[Authorize(Policy = "SuperAdmin")]
public sealed class MigrationController : ApiControllerBase
{
    /// <summary>
    /// Triggers the one-time data migration from the legacy MSSQL CRM database.
    ///
    /// The migration runs as a background task; this endpoint returns immediately
    /// with the initial job status. Poll GET /api/v1/migration/status for live progress.
    ///
    /// Safe to call multiple times — already-migrated records are skipped (idempotent).
    /// </summary>
    /// <param name="request">Target project and MSSQL connection string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// 202 Accepted with initial <see cref="MigrationStatusDto"/> snapshot,
    /// or 409 Conflict if a migration is already running,
    /// or 400 Bad Request on validation failure.
    /// </returns>
    [HttpPost("run")]
    [ProducesResponseType(typeof(ApiResponse<MigrationStatusDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RunMigration(
        [FromBody] RunMigrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new RunMigrationCommand(
            request.ProjectId,
            request.MssqlConnectionString);

        var result = await Mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            var error = result.FirstError ?? "An error occurred.";

            // Return 409 Conflict when a migration is already running
            if (error.Contains("already running", StringComparison.OrdinalIgnoreCase))
                return StatusCode(409, ApiResponse<object>.Fail(error, 409));

            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        }

        // 202 Accepted — job is started but still running
        return StatusCode(202, new ApiResponse<MigrationStatusDto>
        {
            Success    = true,
            Data       = result.Value,
            StatusCode = 202,
            Message    = "Migration job started. Poll GET /api/v1/migration/status for progress."
        });
    }

    /// <summary>
    /// Returns the current state and progress of the migration job.
    /// This endpoint reads in-memory state only — no database round-trip.
    /// Safe to poll frequently.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// 200 OK with <see cref="MigrationStatusDto"/> including progress counters and errors.
    /// </returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<MigrationStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetMigrationStatusQuery(), cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /api/v1/migration/run.</summary>
public sealed record RunMigrationRequest(
    /// <summary>The target project all migrated records will be assigned to.</summary>
    Guid ProjectId,

    /// <summary>
    /// MSSQL connection string for the legacy CRM database (crm.bak source).
    /// Handled securely — never logged.
    /// Example: "Server=192.168.1.10;Database=IONCRM;User Id=sa;Password=***;TrustServerCertificate=True"
    /// </summary>
    string MssqlConnectionString
);
