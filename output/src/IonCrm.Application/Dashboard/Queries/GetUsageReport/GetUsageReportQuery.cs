using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Dashboard.Queries.GetUsageReport;

/// <summary>
/// Returns the customer usage report for a month (defaults to the current UTC month). Each row is
/// a customer joined to its usage snapshot. Optionally filtered to one project.
/// </summary>
public record GetUsageReportQuery(int? Year, int? Month, Guid? ProjectId)
    : IRequest<Result<List<UsageReportRowDto>>>;
