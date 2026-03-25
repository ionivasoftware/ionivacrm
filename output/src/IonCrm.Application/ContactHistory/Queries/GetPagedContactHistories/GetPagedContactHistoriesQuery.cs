using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetPagedContactHistories;

/// <summary>Query to retrieve a paged list of contact history records for a customer.</summary>
public record GetPagedContactHistoriesQuery : IRequest<Result<PagedResult<ContactHistoryDto>>>
{
    public Guid CustomerId { get; init; }
    public ContactType? Type { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
