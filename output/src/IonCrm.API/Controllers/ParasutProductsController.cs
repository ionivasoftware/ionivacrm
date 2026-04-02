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
    /// Paginates through all pages (Paraşüt max page size is 25).
    /// </summary>
    [HttpGet("~/api/v1/crm/parasut/products")]
    public async Task<IActionResult> GetParasutLiveProducts([FromQuery] Guid projectId)
    {
        const int pageSize = 25;
        var allItems = new List<object>();
        int page = 1;
        int totalPages = 1;

        do
        {
            var (data, error) = await _parasutService.GetProductsAsync(
                projectId, page, pageSize, CancellationToken.None);

            if (error is not null)
                return BadRequest(new { error });

            if (data is null) break;

            allItems.AddRange(data.Data.Select(d =>
            {
                var rawPrice = d.Attributes.SalesPrice
                    ?? d.Attributes.ListPrice
                    ?? d.Attributes.SalesPriceInTrl;
                var unitPrice = decimal.TryParse(
                    rawPrice,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var p) ? p : 0m;
                var vatRate = int.TryParse(d.Attributes.VatRate, out var vr) ? vr : 0;
                return (object)new
                {
                    id        = d.Id,
                    name      = d.Attributes.Name,
                    vatRate,
                    unitPrice = (double)unitPrice,
                    currency  = d.Attributes.Currency ?? "TRY",
                    unit      = d.Attributes.Unit
                };
            }));

            totalPages = data.Meta?.TotalPages ?? 1;
            page++;

        } while (page <= totalPages);

        return Ok(new { data = allItems });
    }
}
