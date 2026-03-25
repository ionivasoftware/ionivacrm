using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>Read-only repository for dashboard aggregate queries.</summary>
public interface IDashboardRepository
{
    /// <summary>Returns aggregated dashboard statistics for the given project.</summary>
    Task<DashboardStatsDto> GetStatsAsync(Guid projectId, CancellationToken cancellationToken = default);
}
