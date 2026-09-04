using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Liftdesk backup monitoring — "are backups running, and are they actually restorable?".
/// Proxies the Liftdesk backup API (docs/crm-backup-api.md) so the static CRM API key stays server-side.
///
/// SuperAdmin only: this is infrastructure-wide operations data (database sizes, row counts, run
/// logs) spanning every tenant — deliberately not exposed per project.
/// </summary>
[Route("api/v1/backups")]
[Authorize(Policy = "SuperAdmin")]
public sealed class BackupsController : ApiControllerBase
{
    private readonly ILiftdeskBackupClient _backupClient;

    /// <summary>Initialises a new instance of <see cref="BackupsController"/>.</summary>
    public BackupsController(ILiftdeskBackupClient backupClient)
    {
        _backupClient = backupClient;
    }

    /// <summary>
    /// GET /api/v1/backups/status
    /// Dashboard-card status: isHealthy + Turkish problems[] + last run summaries.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskBackupStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default)
    {
        if (!_backupClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        var envelope = await _backupClient.GetStatusAsync(cancellationToken);

        if (!envelope.Success)
            return BadRequest(ApiResponse<object>.Fail(envelope.Message ?? "Yedek durumu alınamadı.", 400));

        // Data null gelirse "sorun yok" DEĞİL, "bilinmiyor" demektir; sessizliği sağlıklı saymamak
        // için isHealthy=false + açıklayıcı bir problem satırıyla döndürülür (sözleşme §3 uyarısı).
        var status = envelope.Data ?? new LiftdeskBackupStatus(
            IsHealthy: false,
            Problems: new List<string> { "Liftdesk yedek durumu boş döndü — yedekleme kaydı bulunamadı." },
            LastBackup: null, LastSuccessfulBackup: null, HoursSinceLastSuccessfulBackup: null,
            LastVerify: null, LastSuccessfulVerify: null, HoursSinceLastSuccessfulVerify: null,
            LastMirror: null, FailuresLast7Days: 0,
            LatestBackupSizeBytes: null, LatestDatabaseSizeBytes: null);

        return OkResponse(status);
    }

    /// <summary>
    /// GET /api/v1/backups?kind=Backup|Verify|Mirror&amp;limit=50
    /// Run history, newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<LiftdeskBackupRun>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRuns(
        [FromQuery] string? kind,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_backupClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        limit = limit is < 1 or > 200 ? 50 : limit;

        var envelope = await _backupClient.GetRunsAsync(
            string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(), limit, cancellationToken);

        if (!envelope.Success)
            return BadRequest(ApiResponse<object>.Fail(envelope.Message ?? "Yedek geçmişi alınamadı.", 400));

        return OkResponse(envelope.Data ?? new List<LiftdeskBackupRun>());
    }
}
