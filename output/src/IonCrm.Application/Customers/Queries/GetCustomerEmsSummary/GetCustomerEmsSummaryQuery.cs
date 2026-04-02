using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerEmsSummary;

/// <summary>
/// Returns the EMS usage summary for the customer identified by <paramref name="CustomerId"/>.
/// The customer must be EMS-sourced (LegacyId is numeric or "SAASA-{n}").
/// Proxies to EMS GET /api/v1/crm/companies/{emsCompanyId}/summary.
/// </summary>
public record GetCustomerEmsSummaryQuery(Guid CustomerId) : IRequest<Result<EmsSummaryDto>>;

/// <summary>Top-level DTO returned to the API consumer.</summary>
public record EmsSummaryDto(
    int EmsCompanyId,
    EmsSummaryTotalsDto Totals,
    List<EmsSummaryMonthlyStatDto> Monthly);

/// <summary>Overall totals for the EMS company (snapshot counts).</summary>
public record EmsSummaryTotalsDto(
    int CustomerCount,
    int ElevatorCount,
    int UserCount);

/// <summary>Monthly activity counts for a single calendar month.</summary>
public record EmsSummaryMonthlyStatDto(
    int Year,
    int Month,
    int MaintenanceCount,
    int BreakdownCount,
    int ProposalCount);
