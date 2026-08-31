using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Dashboard.Queries.GetUsageReport;

/// <summary>Handles <see cref="GetUsageReportQuery"/>.</summary>
public class GetUsageReportQueryHandler
    : IRequestHandler<GetUsageReportQuery, Result<List<UsageReportRowDto>>>
{
    private readonly IDashboardRepository _dashboardRepository;

    /// <summary>Initialises a new instance of <see cref="GetUsageReportQueryHandler"/>.</summary>
    public GetUsageReportQueryHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    /// <inheritdoc />
    public async Task<Result<List<UsageReportRowDto>>> Handle(
        GetUsageReportQuery request, CancellationToken cancellationToken)
    {
        var now   = DateTime.UtcNow;
        var year  = request.Year ?? now.Year;
        var month = request.Month is >= 1 and <= 12 ? request.Month.Value : now.Month;

        var rows = await _dashboardRepository.GetUsageReportAsync(
            year, month, request.ProjectId, cancellationToken);

        return Result<List<UsageReportRowDto>>.Success(rows);
    }
}
