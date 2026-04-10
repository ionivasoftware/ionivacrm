using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomers;

/// <summary>Query to get a paginated, filtered list of customers.</summary>
public record GetCustomersQuery : IRequest<Result<PagedResult<CustomerDto>>>
{
    /// <summary>Optional project (tenant) filter. If null, user's accessible projects are used via global query filter.</summary>
    public Guid? ProjectId { get; init; }
    public string? Search { get; init; }
    public CustomerStatus? Status { get; init; }
    public string? Segment { get; init; }
    public CustomerLabel? Label { get; init; }
    public Guid? AssignedUserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    /// <summary>
    /// Sort key. Default (null / "activity_desc") = last activity date descending.
    /// Other options: "name", "name_desc", "created", "created_desc", "activity".
    /// </summary>
    public string? SortBy { get; init; }
}
