using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetAllContactHistories;

/// <summary>
/// Query to retrieve all contact history records across all accessible customers in a project.
/// Used by the "All Conversations" page — filtered view across the entire tenant.
/// Tenant isolation is enforced by the EF Core global query filter (ProjectId).
/// </summary>
public record GetAllContactHistoriesQuery : IRequest<Result<PagedResult<ContactHistoryDto>>>
{
    /// <summary>Optional project (tenant) filter. Must be one of the user's accessible projects.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Optional customer filter to narrow results to a specific customer.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Optional contact type filter (Call, Email, Meeting, Note, WhatsApp, Visit).</summary>
    public ContactType? Type { get; init; }

    /// <summary>Optional date range start (inclusive, UTC).</summary>
    public DateTime? FromDate { get; init; }

    /// <summary>Optional date range end (inclusive, UTC).</summary>
    public DateTime? ToDate { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
