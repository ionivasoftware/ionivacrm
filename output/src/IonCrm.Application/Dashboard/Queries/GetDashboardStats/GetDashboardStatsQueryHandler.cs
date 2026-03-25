using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Dashboard.Queries.GetDashboardStats;

/// <summary>Handles <see cref="GetDashboardStatsQuery"/>.</summary>
public class GetDashboardStatsQueryHandler
    : IRequestHandler<GetDashboardStatsQuery, Result<DashboardStatsDto>>
{
    private readonly IDashboardRepository _dashboardRepository;

    public GetDashboardStatsQueryHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<Result<DashboardStatsDto>> Handle(
        GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        var stats = await _dashboardRepository.GetStatsAsync(request.ProjectId, cancellationToken);
        return Result<DashboardStatsDto>.Success(stats);
    }
}
