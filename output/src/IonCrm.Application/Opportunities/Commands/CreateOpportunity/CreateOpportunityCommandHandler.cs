using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Opportunities.Mappings;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Opportunities.Commands.CreateOpportunity;

public class CreateOpportunityCommandHandler
    : IRequestHandler<CreateOpportunityCommand, Result<OpportunityDto>>
{
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateOpportunityCommandHandler> _logger;

    public CreateOpportunityCommandHandler(
        IOpportunityRepository opportunityRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<CreateOpportunityCommandHandler> logger)
    {
        _opportunityRepository = opportunityRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<OpportunityDto>> Handle(
        CreateOpportunityCommand request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<OpportunityDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<OpportunityDto>.Failure("Access denied to this customer.");

        var opportunity = new Opportunity
        {
            CustomerId = request.CustomerId,
            ProjectId = customer.ProjectId,
            Title = request.Title,
            Stage = request.Stage,
            Probability = request.Probability,
            ExpectedCloseDate = request.ExpectedCloseDate,
            AssignedUserId = request.AssignedUserId
        };

        await _opportunityRepository.AddAsync(opportunity, cancellationToken);

        _logger.LogInformation("Opportunity {Id} created for customer {CustomerId}", opportunity.Id, opportunity.CustomerId);

        return Result<OpportunityDto>.Success(opportunity.ToDto());
    }
}
