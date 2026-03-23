using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomers;

/// <summary>Query to get a paginated, filtered list of customers.</summary>
public record GetCustomersQuery : IRequest<Result<PagedResult<CustomerDto>>>
{
    public string? Search { get; init; }
    public CustomerStatus? Status { get; init; }
    public CustomerSegment? Segment { get; init; }
    public Guid? AssignedUserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
