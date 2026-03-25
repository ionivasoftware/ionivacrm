using IonCrm.Application.ContactHistory.Commands.CreateContactHistory;
using IonCrm.Application.ContactHistory.Commands.DeleteContactHistory;
using IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;
using IonCrm.Application.ContactHistory.Queries.GetContactHistoryById;
using IonCrm.Application.ContactHistory.Queries.GetPagedContactHistories;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for Customer Contact History (calls, emails, meetings, notes).
/// Nested under customers: /api/v1/customers/{customerId}/contact-histories
/// </summary>
[Route("api/v1/customers/{customerId:guid}/contact-histories")]
public class ContactHistoriesController : ApiControllerBase
{
    /// <summary>Gets a paged list of contact history records for a customer.</summary>
    [HttpGet]
    public async Task<IActionResult> GetContactHistories(
        Guid customerId,
        [FromQuery] ContactType? type = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPagedContactHistoriesQuery
        {
            CustomerId = customerId,
            Type = type,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize
        };
        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Gets a single contact history record by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetContactHistoryById(
        Guid customerId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetContactHistoryByIdQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Logs a new contact history entry for a customer.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateContactHistory(
        Guid customerId,
        [FromBody] CreateContactHistoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandWithCustomer = command with { CustomerId = customerId };
        var result = await Mediator.Send(commandWithCustomer, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    /// <summary>Updates a contact history record.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateContactHistory(
        Guid customerId,
        Guid id,
        [FromBody] UpdateContactHistoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandWithId = command with { Id = id };
        var result = await Mediator.Send(commandWithId, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Soft-deletes a contact history record.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteContactHistory(
        Guid customerId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeleteContactHistoryCommand(id), cancellationToken);
        return ResultToResponse(result);
    }
}
