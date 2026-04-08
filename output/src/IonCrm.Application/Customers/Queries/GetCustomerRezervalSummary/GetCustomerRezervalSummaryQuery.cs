using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerRezervalSummary;

/// <summary>
/// Returns the Rezerval reservation/SMS summary for a Rezerval-sourced customer.
/// The customer must have a LegacyId starting with "SAASB-" or "REZV-".
/// Proxies to GET https://rezback.rezerval.com/v1/Crm/CompanySummary?companyId={id}.
/// </summary>
public record GetCustomerRezervalSummaryQuery(Guid CustomerId)
    : IRequest<Result<RezervalSummaryDto>>;

/// <summary>Top-level DTO returned to the API consumer.</summary>
public record RezervalSummaryDto(
    int RezervalCompanyId,
    string? CompanyName,
    RezervalSummaryPeriodDto? LastWeek,
    RezervalSummaryPeriodDto? LastMonth,
    RezervalSummaryPeriodDto? Last3Months);

/// <summary>Aggregated metrics for a single time-window.</summary>
public record RezervalSummaryPeriodDto(
    DateTime? StartDate,
    DateTime? EndDate,
    int ReservationCount,
    int PersonCount,
    int CompletedReservationCount,
    int CancelledReservationCount,
    int OnlineReservationCount,
    int WalkInCount,
    int WalkInPersonCount,
    int SmsSentCount);
