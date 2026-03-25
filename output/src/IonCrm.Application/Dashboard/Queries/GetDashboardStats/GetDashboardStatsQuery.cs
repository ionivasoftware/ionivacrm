using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Dashboard.Queries.GetDashboardStats;

/// <summary>Returns aggregated dashboard statistics for a project.</summary>
public record GetDashboardStatsQuery(Guid ProjectId) : IRequest<Result<DashboardStatsDto>>;
