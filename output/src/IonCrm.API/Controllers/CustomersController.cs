using IonCrm.Application.Customers.Commands.AddCustomerSms;
using IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.ExtendEmsExpiration;
using IonCrm.Application.Customers.Commands.TransferLead;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Application.Customers.Queries.GetCustomerParasutTransactions;
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
    /// Transfers all ContactHistories, Tasks and Opportunities from a Lead customer
    /// to an active target customer, then soft-deletes the lead.
    /// Returns 400 if the source is not a Lead, or if lead and target are in different projects.
    /// </summary>
    [HttpPost("{leadId:guid}/transfer/{targetCustomerId:guid}")]
    public async Task<IActionResult> TransferLead(
        Guid leadId,
        Guid targetCustomerId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new TransferLeadCommand(leadId, targetCustomerId), cancellationToken);

        if (result.IsSuccess)
            return NoContent();

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

    /// <summary>
    /// POST /api/v1/customers/{id}/add-sms
    /// Adds SMS credits to an EMS customer and creates a Paraşüt draft invoice.
    /// </summary>
    [HttpPost("{id:guid}/add-sms")]
    public async Task<IActionResult> AddSms(
        Guid id,
        [FromBody] AddCustomerSmsRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new AddCustomerSmsCommand(id, body.Count);
        var result  = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// GET /api/v1/customers/{id}/parasut-transactions
    /// Returns paginated Paraşüt cari hareketleri (invoices) for the given CRM customer.
    /// The customer must have a linked Paraşüt contact (ParasutContactId) — use
    /// POST /api/v1/parasut/contacts/sync or POST /api/v1/parasut/contacts/link first.
    /// </summary>
    [HttpGet("{id:guid}/parasut-transactions")]
    public async Task<IActionResult> GetParasutTransactions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetCustomerParasutTransactionsQuery(id, page, pageSize), cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /api/v1/customers/{id}/extend-expiration.</summary>
public record ExtendEmsExpirationRequest(string DurationType, int Amount);

/// <summary>Request body for POST /api/v1/customers/{id}/add-sms.</summary>
public record AddCustomerSmsRequest(int Count);
