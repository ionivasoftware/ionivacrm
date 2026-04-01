using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.ParasutProducts.Commands.UpsertParasutProduct;
using IonCrm.Application.Features.ParasutProducts.Queries.GetParasutProducts;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for managing Paraşüt product (ürün) mappings per project.
/// Route: api/v1/crm/parasut-products
/// Also exposes: GET api/v1/crm/parasut/products  — live product list from Paraşüt API
/// </summary>
[Route("api/v1/crm/parasut-products")]
public class ParasutProductsController : ApiControllerBase
{
    private readonly IParasutService _parasutService;

    public ParasutProductsController(IParasutService parasutService)
    {
        _parasutService = parasutService;
    }

    /// <summary>
    /// GET /api/v1/crm/parasut-products?projectId={projectId}
    /// Returns saved CRM→Paraşüt product mappings for the given project.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid projectId)
    {
        var result = await Mediator.Send(new GetParasutProductsQuery(projectId));
        return ResultToResponse(result);
    }

    /// <summary>
    /// POST /api/v1/crm/parasut-products
    /// Creates a new product mapping (or updates if same project+name already exists).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] UpsertParasutProductCommand command)
    {
        var result = await Mediator.Send(command);
        return ResultToResponse(result);
    }

    /// <summary>
    /// PUT /api/v1/crm/parasut-products/{id}
    /// Updates an existing product mapping by ID.
    /// Frontend passes existingId → routes here; the UpsertCommand handles insert-or-update by name.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertParasutProductCommand command)
    {
        var result = await Mediator.Send(command);
        return ResultToResponse(result);
    }

    /// <summary>
    /// GET /api/v1/crm/parasut/products?projectId={projectId}
    /// Returns the live product/service list from the Paraşüt API for use in dropdowns.
    /// </summary>
    [HttpGet("~/api/v1/crm/parasut/products")]
    public async Task<IActionResult> GetParasutLiveProducts([FromQuery] Guid projectId)
    {
        var (data, error) = await _parasutService.GetProductsAsync(projectId, 1, 200, CancellationToken.None);
        if (error is not null)
            return BadRequest(new { error });

        var items = data?.Data.Select(d => new
        {
            id   = d.Id,
            name = d.Attributes.Name,
            vatRate = d.Attributes.VatRate,
            salesPrice = d.Attributes.SalesPrice,
            currency = d.Attributes.Currency,
            unit = d.Attributes.Unit
        }).ToList();

        return Ok(new { data = items });
    }
}
