using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Dashboard.Queries.GetDashboardStats;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Dashboard aggregate endpoints.
/// GET /api/v1/dashboard/stats — returns KPI widgets and chart data for the given project.
/// GET /api/v1/dashboard/notifications — returns last 10 recent activities for the notification panel.
/// </summary>
[Route("api/v1/dashboard")]
public class DashboardController : ApiControllerBase
{
    private readonly IDashboardRepository _dashboardRepository;

    public DashboardController(IDashboardRepository dashboardRepository)
        => _dashboardRepository = dashboardRepository;

    /// <summary>Returns aggregated dashboard statistics for a project.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetDashboardStatsQuery(projectId), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Returns the last 10 recent activities for the notification panel.</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(
        CancellationToken cancellationToken = default)
    {
        var activities = await _dashboardRepository.GetRecentActivitiesAsync(10, cancellationToken);
        return OkResponse(activities);
    }
}

[Route("api/v1/reports")]
public class ReportsController : ApiControllerBase
{
    private readonly IDashboardRepository _dashboardRepository;

    public ReportsController(IDashboardRepository dashboardRepository)
        => _dashboardRepository = dashboardRepository;

    /// <summary>Returns date-filtered report data for a project.</summary>
    [HttpGet]
    public async Task<IActionResult> GetReports(
        [FromQuery] Guid projectId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (startDate > endDate)
            return BadRequest(ApiResponse<object>.Fail("startDate must be before endDate"));

        var reports = await _dashboardRepository.GetReportsAsync(projectId, startDate, endDate, cancellationToken);
        return OkResponse(reports);
    }
}
