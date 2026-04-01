using IonCrm.Application.Projects.Commands.AddSms;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Company (Project/tenant) level operations.
/// Route: /api/v1/crm/companies
/// </summary>
[Route("api/v1/crm/companies")]
public class CompaniesController : ApiControllerBase
{
    /// <summary>
    /// Adds SMS credits to a company.
    /// POST /api/v1/crm/companies/{id}/add-sms
    /// Body: { "count": int }
    /// Response: { companyId, smsCount, added }
    /// </summary>
    [HttpPost("{id:guid}/add-sms")]
    public async Task<IActionResult> AddSms(
        Guid id,
        [FromBody] AddSmsRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new AddSmsCommand(id, body.Count);
        var result  = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /api/v1/crm/companies/{id}/add-sms.</summary>
public record AddSmsRequest(int Count);
