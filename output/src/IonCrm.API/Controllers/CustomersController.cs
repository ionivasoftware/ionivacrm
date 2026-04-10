using IonCrm.Application.Customers.Commands.AddCustomerSms;
using IonCrm.Application.Customers.Commands.CancelCustomerContract;
using IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.ExtendEmsExpiration;
using IonCrm.Application.Customers.Commands.PushCustomerToRezerval;
using IonCrm.Application.Customers.Commands.TransferLead;
using IonCrm.Application.Customers.Commands.UpdateContractPaymentType;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetActiveContractByCustomerId;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Application.Customers.Queries.GetCustomerEmsUsers;
using IonCrm.Application.Customers.Queries.GetCustomerEmsSummary;
using IonCrm.Application.Customers.Queries.GetCustomerRezervalSummary;
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
        [FromQuery] string? sortBy = null,
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
            PageSize = pageSize,
            SortBy = sortBy
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
    /// GET /api/v1/customers/{id}/ems-users
    /// Returns the EMS user list for an EMS-sourced customer.
    /// Returns 400 if the customer has no EMS mapping (LegacyId is null, "PC-..." or non-numeric).
    /// </summary>
    [HttpGet("{id:guid}/ems-users")]
    public async Task<IActionResult> GetEmsUsers(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerEmsUsersQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// GET /api/v1/customers/{id}/ems-summary
    /// Returns the EMS usage summary for an EMS-sourced customer.
    /// Proxies to EMS GET /api/v1/crm/companies/{emsCompanyId}/summary.
    /// Response includes: monthly maintenance/breakdown/proposal counts + totals (customer/elevator/user counts).
    /// Returns 400 if the customer has no EMS mapping (LegacyId is null, "PC-...", "REZV-..." or non-numeric).
    /// </summary>
    [HttpGet("{id:guid}/ems-summary")]
    public async Task<IActionResult> GetEmsSummary(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerEmsSummaryQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// GET /api/v1/customers/{id}/rezerval-summary
    /// Returns the Rezerval reservation/SMS summary for a Rezerval-sourced customer.
    /// Proxies to Rezerval GET /v1/Crm/CompanySummary?companyId={id}.
    /// Response includes lastWeek / lastMonth / last3Months periods, each with reservation,
    /// person, completed, cancelled, online, walk-in and SMS counts.
    /// Returns 400 when the customer is not Rezerval-sourced.
    /// </summary>
    [HttpGet("{id:guid}/rezerval-summary")]
    public async Task<IActionResult> GetRezervalSummary(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCustomerRezervalSummaryQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// POST /api/v1/customers/{id}/push-to-rezerval
    /// Pushes the customer to the RezervAl CRM system.
    /// If the customer already has a "REZV-{n}" LegacyId an update (PUT) is performed;
    /// otherwise a new company is created (POST) and the returned companyId is stored
    /// as the customer's LegacyId in the format "REZV-{companyId}".
    /// Returns 400 if the project has no RezervAl API key configured.
    /// </summary>
    [HttpPost("{id:guid}/push-to-rezerval")]
    public async Task<IActionResult> PushToRezerval(
        Guid id,
        [FromBody] PushCustomerToRezervalRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new PushCustomerToRezervalCommand
        {
            CustomerId       = id,
            Name             = body.Name,
            Title            = body.Title,
            Phone            = body.Phone,
            Email            = body.Email,
            TaxUnit          = body.TaxUnit,
            TaxNumber        = body.TaxNumber,
            TCNo             = body.TCNo,
            IsPersonCompany  = body.IsPersonCompany,
            Address          = body.Address,
            Language         = body.Language,
            CountryPhoneCode = body.CountryPhoneCode,
            ExperationDate   = body.ExperationDate,
            AdminNameSurname = body.AdminNameSurname,
            AdminLoginName   = body.AdminLoginName,
            AdminPassword    = body.AdminPassword,
            AdminEmail       = body.AdminEmail,
            AdminPhone       = body.AdminPhone,
            LogoBase64       = body.LogoBase64,
            LogoFileName     = body.LogoFileName
        };

        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Creates (or renews) a recurring monthly subscription contract for a Rezerval customer.
    /// Calls the Rezerval subscription endpoint to create an iyzico subscription + payment plan
    /// on the Rezerval side, then stores the contract locally. For EFT contracts, the background
    /// sync job will auto-create monthly draft invoices priced at the contract's monthly amount.
    /// </summary>
    [HttpPost("{id:guid}/contracts")]
    public async Task<IActionResult> CreateContract(
        Guid id,
        [FromBody] CreateContractRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateCustomerContractCommand(
            CustomerId:     id,
            MonthlyAmount:  body.MonthlyAmount,
            PaymentType:    body.PaymentType,
            StartDate:      body.StartDate,
            DurationMonths: body.DurationMonths);

        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    /// <summary>Returns the active contract for a customer, or null when none exists.</summary>
    [HttpGet("{id:guid}/contracts/active")]
    public async Task<IActionResult> GetActiveContract(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetActiveContractByCustomerIdQuery(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Cancels the active recurring subscription contract for a Rezerval customer.
    /// Calls the Rezerval cancel endpoint (which deletes the iyzico pricing plan + product
    /// and tolerates iyzico-side failures via warnings) and marks the local contract as
    /// Cancelled. Returns the updated contract DTO + any iyzico warnings surfaced.
    /// </summary>
    [HttpPost("{id:guid}/contracts/cancel")]
    public async Task<IActionResult> CancelContract(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new CancelCustomerContractCommand(id), cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Changes the payment type (CreditCard / EftWire) of the active contract.</summary>
    [HttpPatch("{id:guid}/contracts/payment-type")]
    public async Task<IActionResult> UpdateContractPaymentType(
        Guid id,
        [FromBody] UpdatePaymentTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new UpdateContractPaymentTypeCommand(id, body.PaymentType), cancellationToken);
        return ResultToResponse(result);
    }

}

/// <summary>Request body for PATCH /api/v1/customers/{id}/contracts/payment-type.</summary>
public record UpdatePaymentTypeRequest(ContractPaymentType PaymentType);

/// <summary>Request body for POST /api/v1/customers/{id}/extend-expiration.</summary>
public record ExtendEmsExpirationRequest(string DurationType, int Amount);

/// <summary>Request body for POST /api/v1/customers/{id}/add-sms.</summary>
public record AddCustomerSmsRequest(int Count);

/// <summary>Request body for POST /api/v1/customers/{id}/contracts.</summary>
public record CreateContractRequest(
    decimal MonthlyAmount,
    ContractPaymentType PaymentType,
    DateTime StartDate,
    int? DurationMonths);

/// <summary>Request body for POST /api/v1/customers/{id}/push-to-rezerval.</summary>
public record PushCustomerToRezervalRequest(
    string Name,
    string Title,
    string Phone,
    string Email,
    string TaxUnit,
    string TaxNumber,
    string? TCNo,
    bool IsPersonCompany,
    string Address,
    int Language = 1,
    int CountryPhoneCode = 90,
    DateTime? ExperationDate = null,
    string AdminNameSurname = "",
    string AdminLoginName = "",
    string AdminPassword = "",
    string AdminEmail = "",
    string AdminPhone = "",
    string? LogoBase64 = null,
    string? LogoFileName = null);
