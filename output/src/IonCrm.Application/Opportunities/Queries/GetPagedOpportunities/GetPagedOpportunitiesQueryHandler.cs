using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Opportunities.Queries.GetPagedOpportunities;

public class GetPagedOpportunitiesQueryHandler
    : IRequestHandler<GetPagedOpportunitiesQuery, Result<PagedResult<OpportunityDto>>>
{
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    public GetPagedOpportunitiesQueryHandler(
        IOpportunityRepository opportunityRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _opportunityRepository = opportunityRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<OpportunityDto>>> Handle(
        GetPagedOpportunitiesQuery request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<PagedResult<OpportunityDto>>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<PagedResult<OpportunityDto>>.Failure("Access denied.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _opportunityRepository.GetPagedByCustomerIdAsync(
            request.CustomerId, page, pageSize, cancellationToken);

        var dtos = items.Select(o => o.ToDto()).ToList().AsReadOnly();
        return Result<PagedResult<OpportunityDto>>.Success(
            new PagedResult<OpportunityDto>(dtos, totalCount, page, pageSize));
    }
}
