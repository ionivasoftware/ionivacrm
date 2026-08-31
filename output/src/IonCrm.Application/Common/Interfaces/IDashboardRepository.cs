using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>Read-only repository for dashboard aggregate queries.</summary>
public interface IDashboardRepository
{
    /// <summary>Returns aggregated dashboard statistics for the given project.</summary>
    Task<DashboardStatsDto> GetStatsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Returns the last N recent activities across all accessible projects (for notification panel).</summary>
    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivitiesAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated report data for the given project and date range.</summary>
    Task<ReportsDto> GetReportsAsync(Guid projectId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer usage report for a given month — every customer that has a usage
    /// snapshot for (year, month), ordered by company name. Tenant-scoped by the caller's access.
    /// </summary>
    Task<List<UsageReportRowDto>> GetUsageReportAsync(
        int year, int month, Guid? projectId, CancellationToken cancellationToken = default);
}
