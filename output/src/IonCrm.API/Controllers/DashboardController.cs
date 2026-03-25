using IonCrm.Application.Dashboard.Queries.GetDashboardStats;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Dashboard aggregate endpoints.
/// GET /api/v1/dashboard/stats — returns KPI widgets and chart data for the given project.
/// </summary>
[Route("api/v1/dashboard")]
public class DashboardController : ApiControllerBase
{
    /// <summary>Returns aggregated dashboard statistics for a project.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetDashboardStatsQuery(projectId), cancellationToken);
        return ResultToResponse(result);
    }
}
