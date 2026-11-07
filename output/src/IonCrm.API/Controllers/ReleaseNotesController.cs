using IonCrm.Application.ReleaseNotes.Commands.MarkReleaseNotesSeen;
using IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotes;
using IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotesStatus;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// "Yenilikler" — Liftdesk'in haftalık sürüm notları (EMS sözleşmesi "Sürüm Notları API (CRM)"). Liftdesk ucunu
/// proxy'ler; statik CRM anahtarı sunucuda kalır, yük 10 dk bellekte. Her oturum açmış kullanıcıya açık (satış ve
/// destek müşteriye "yeni ne var" anlatır). Görüldü durumu kullanıcı bazında CRM'de (Users.LastSeenReleaseId).
/// </summary>
[Route("api/v1/release-notes")]
public sealed class ReleaseNotesController : ApiControllerBase
{
    /// <summary>GET /api/v1/release-notes — tüm sürümler (en yeni başta) + bu kullanıcının görüldü durumu.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default)
        => ResultToResponse(await Mediator.Send(new GetReleaseNotesQuery(), cancellationToken));

    /// <summary>GET /api/v1/release-notes/status — menü rozeti (en yeni sürüm + görülmemiş sayısı; Liftdesk kapalıysa 0).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
        => ResultToResponse(await Mediator.Send(new GetReleaseNotesStatusQuery(), cancellationToken));

    /// <summary>POST /api/v1/release-notes/seen { releaseId } — ekranın en üstte gösterdiği sürüm görüldü sayılır.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> Seen([FromBody] MarkReleaseNotesSeenBody body, CancellationToken cancellationToken = default)
        => ResultToResponse(await Mediator.Send(new MarkReleaseNotesSeenCommand(body.ReleaseId ?? string.Empty), cancellationToken));
}

public sealed record MarkReleaseNotesSeenBody(string? ReleaseId);
