using IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.ExtendEmsExpiration;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Application.Customers.Queries.GetCustomerWithDetails;
using IonCrm.Application.Customers.Queries.GetCustomers;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// CRUD endpoints for Customers.
/// All endpoints require authentication; tenant isolation is enforced in handlers.
/// GET /api/v1/customers — paginated list with search/filter
/// POST /api/v1/customers — create
/// GET /api/v1/customers/{id} — get by ID
/// PUT /api/v1/customers/{id} — update
/// DELETE /api/v1/customers/{id} — soft-delete
/// </summary>
[Route("api/v1/customers")]
public class CustomersController : ApiControllerBase
{
    /// <summary>Gets a paginated, filtered list of customers.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] Guid? projectId,
        [FromQuery] string? search,
        [FromQuery] CustomerStatus? status,
        [FromQuery] string? segment,
        [FromQuery] CustomerLabel? label,
        [FromQuery] Guid? assignedUserId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetCustomersQuery
        {
            ProjectId = projectId,
            Search = search,
            Status = status,
            Segment = segment,
            Label = label,
            AssignedUserId = assignedUserId,
            Page = page,
            PageSize = pageSize
        };

        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Gets a customer by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerByIdQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Creates a new customer.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    /// <summary>Updates an existing customer.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCustomer(
        Guid id,
        [FromBody] UpdateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandWithId = command with { Id = id };
        var result = await Mediator.Send(commandWithId, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Soft-deletes a customer.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeleteCustomerCommand(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Gets a customer with full details including recent contact history and open tasks.
    /// </summary>
    [HttpGet("{id:guid}/details")]
    public async Task<IActionResult> GetCustomerWithDetails(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerWithDetailsQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Converts a Lead customer to an Active customer (potential → customer pipeline step).
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    public async Task<IActionResult> ConvertLeadToCustomer(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new ConvertLeadToCustomerCommand(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Extends the EMS expiration date for an EMS-sourced customer.
    /// Also creates a Paraşüt draft invoice when durationType is "Months" or "Years" (amount=1).
    /// </summary>
    [HttpPost("{id:guid}/extend-expiration")]
    public async Task<IActionResult> ExtendEmsExpiration(
        Guid id,
        [FromBody] ExtendEmsExpirationRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new ExtendEmsExpirationCommand(id, body.DurationType, body.Amount);
        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /api/v1/customers/{id}/extend-expiration.</summary>
public record ExtendEmsExpirationRequest(string DurationType, int Amount);
