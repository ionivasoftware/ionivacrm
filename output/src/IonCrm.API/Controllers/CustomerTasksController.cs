using IonCrm.Application.Tasks.Commands.CreateCustomerTask;
using IonCrm.Application.Tasks.Commands.DeleteCustomerTask;
using IonCrm.Application.Tasks.Commands.UpdateCustomerTask;
using IonCrm.Application.Tasks.Queries.GetCustomerTasks;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for Customer Tasks.
/// Nested under customers: /api/v1/customers/{customerId}/tasks
/// </summary>
[Route("api/v1/customers/{customerId:guid}/tasks")]
public class CustomerTasksController : ApiControllerBase
{
    /// <summary>Gets all tasks for a customer.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTasks(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerTasksQuery(customerId), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Creates a new task for a customer.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateTask(
        Guid customerId,
        [FromBody] CreateCustomerTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandWithCustomer = command with { CustomerId = customerId };
        var result = await Mediator.Send(commandWithCustomer, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    /// <summary>Updates an existing task.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTask(
        Guid customerId,
        Guid id,
        [FromBody] UpdateCustomerTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandWithId = command with { Id = id };
        var result = await Mediator.Send(commandWithId, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Soft-deletes a task.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTask(
        Guid customerId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeleteCustomerTaskCommand(id), cancellationToken);
        return ResultToResponse(result);
    }
}
