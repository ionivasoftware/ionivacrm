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
    int UserCount,
    DateTime? LastLoginAt = null,
    /// <summary>"CurrentAccount" | "Invoice" — firmanın muhasebe modu; null ise kaynak bildirmiyor.</summary>
    string? AccountingMode = null);

/// <summary>Monthly activity counts for a single calendar month.</summary>
public record EmsSummaryMonthlyStatDto(
    int Year,
    int Month,
    int MaintenanceCount,
    int BreakdownCount,
    /// <summary>Sum of the three offer types — kept for backward compatibility with the summary tab.</summary>
    int ProposalCount,
    /// <summary>Part-change offers alone (split out for the churn model's breadth/stickiness scoring).</summary>
    int PartChangeOfferCount = 0,
    /// <summary>Revision offers alone.</summary>
    int RevisionOfferCount = 0,
    /// <summary>Assembly offers alone.</summary>
    int AssemblyOfferCount = 0);
