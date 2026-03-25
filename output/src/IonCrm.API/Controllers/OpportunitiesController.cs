using IonCrm.Application.Opportunities.Commands.CreateOpportunity;
using IonCrm.Application.Opportunities.Commands.UpdateOpportunity;
using IonCrm.Application.Opportunities.Queries.GetPagedOpportunities;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for customer opportunities (sales pipeline deals).
/// GET    /api/v1/customers/{customerId}/opportunities
/// POST   /api/v1/customers/{customerId}/opportunities
/// PUT    /api/v1/customers/{customerId}/opportunities/{id}
/// </summary>
[Route("api/v1/customers/{customerId:guid}/opportunities")]
public class OpportunitiesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetOpportunities(
        Guid customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPagedOpportunitiesQuery
        {
            CustomerId = customerId,
            Page = page,
            PageSize = pageSize
        }, cancellationToken);
        return ResultToResponse(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOpportunity(
        Guid customerId,
        [FromBody] CreateOpportunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command with { CustomerId = customerId }, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateOpportunity(
        Guid customerId,
        Guid id,
        [FromBody] UpdateOpportunityCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command with { Id = id, CustomerId = customerId }, cancellationToken);
        return ResultToResponse(result);
    }
}
