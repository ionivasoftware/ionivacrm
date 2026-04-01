using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Features.ParasutProducts.Commands.UpsertParasutProduct;
using IonCrm.Application.Features.ParasutProducts.Queries.GetParasutProducts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for managing Paraşüt product (ürün) mappings per project.
///
/// Stores 6 fixed product configurations (memberships + SMS packages) that are used
/// when automatically creating Paraşüt draft invoices from CRM workflows.
///
/// Product catalog:
///   - 1 Aylık Üyelik
///   - 1 Yıllık Üyelik
///   - 1000 SMS
///   - 2500 SMS
///   - 5000 SMS
///   - 10000 SMS
/// </summary>
[Route("api/v1/crm/settings/parasut-products")]
public class ParasutProductsController : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/crm/settings/parasut-products?projectId={projectId}
    /// Returns all Paraşüt product configurations for the given project.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid projectId)
    {
        var result = await Mediator.Send(new GetParasutProductsQuery(projectId));
        return ResultToResponse(result);
    }

    /// <summary>
    /// PUT /api/v1/crm/settings/parasut-products
    /// Creates or updates a product mapping by project + product name.
    /// If a record with the same ProductName already exists in the project it is updated;
    /// otherwise a new record is created.
    /// Requires SuperAdmin.
    /// </summary>
    [HttpPut]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Upsert([FromBody] UpsertParasutProductCommand command)
    {
        var result = await Mediator.Send(command);
        return ResultToResponse(result);
    }
}
