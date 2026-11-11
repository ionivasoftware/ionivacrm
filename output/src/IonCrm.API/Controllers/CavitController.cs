using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Counts for the Cavit desktop assistant (anonymous + <c>X-Cavit-Key</c> = Cavit:ApiKey).
/// GET /api/v1/cavit/stats?project=Liftdesk — numbers only, no personal data.
/// </summary>
[Route("api/v1/cavit")]
[AllowAnonymous]
public sealed class CavitController : ApiControllerBase
{
    private readonly ICavitStatsRepository _repo;
    private readonly IConfiguration _configuration;

    public CavitController(ICavitStatsRepository repo, IConfiguration configuration)
    {
        _repo = repo; _configuration = configuration;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] string project = "Liftdesk", CancellationToken cancellationToken = default)
    {
        if (CavitAuth.Check(Request, _configuration) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(project) || project.Length > 100)
            return BadRequest(ApiResponse<object>.Fail("project gerekli (≤100).", 400));

        var stats = await _repo.GetAsync(project, DateTime.UtcNow, cancellationToken);
        if (stats is null)
        {
            var names = await _repo.ProjectNamesAsync(cancellationToken);
            return NotFound(ApiResponse<object>.Fail($"'{project}' projesi yok. Mevcut: {string.Join(", ", names)}", 404));
        }
        return OkResponse(stats);
    }
}
