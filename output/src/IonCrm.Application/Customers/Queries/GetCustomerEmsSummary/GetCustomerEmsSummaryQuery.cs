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
    List<EmsSummaryMonthlyStatDto> Monthly,
    EmsSummaryStorageDto? Storage = null);

/// <summary>
/// Document-storage footprint on the Liftdesk volume. Null when the source system does not report it.
/// <see cref="QuotaBytesPerAssembly"/> is a per-assembly cap shown for context — it is NOT the
/// denominator of <see cref="AssemblyDocumentBytes"/> (which spans every assembly of the tenant).
/// </summary>
public record EmsSummaryStorageDto(
    long AssemblyDocumentBytes,
    int AssemblyDocumentCount,
    long QuotaBytesPerAssembly);

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
