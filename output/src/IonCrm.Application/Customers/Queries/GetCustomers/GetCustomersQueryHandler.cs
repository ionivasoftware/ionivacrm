using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomers;

/// <summary>Handles <see cref="GetCustomersQuery"/>.</summary>
public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, Result<PagedResult<CustomerDto>>>
{
    private readonly ICustomerRepository _customerRepository;

    public GetCustomersQueryHandler(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerDto>>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _customerRepository.GetPagedAsync(
            request.ProjectId,
            request.Search,
            request.Status,
            request.Segment,
            request.Label,
            request.AssignedUserId,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(c => c.ToDto()).ToList().AsReadOnly();
        var pagedResult = new PagedResult<CustomerDto>(dtos, totalCount, page, pageSize);

        return Result<PagedResult<CustomerDto>>.Success(pagedResult);
    }
}
