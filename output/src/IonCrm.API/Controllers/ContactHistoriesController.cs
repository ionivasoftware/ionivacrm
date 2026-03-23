using IonCrm.Application.ContactHistory.Commands.CreateContactHistory;
using IonCrm.Application.ContactHistory.Commands.DeleteContactHistory;
using IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;
using IonCrm.Application.ContactHistory.Queries.GetContactHistories;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for Customer Contact History (calls, emails, meetings, notes).
/// Nested under customers: /api/v1/customers/{customerId}/contact-histories
/// </summary>
[Route("api/v1/customers/{customerId:guid}/contact-histories")]
public class ContactHistoriesController : ApiControllerBase
{
    /// <summary>Gets all contact history records for a customer.</summary>
    [HttpGet]
    public async Task<IActionResult> GetContactHistories(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetContactHistoriesQuery(customerId), cancellationToken);
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
