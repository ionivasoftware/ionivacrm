using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Queries.GetParasutProducts;

/// <summary>
/// Query to retrieve all Paraşüt products for a given project.
/// Returns the product catalog used for invoice line items.
/// </summary>
public record GetParasutProductsQuery(Guid ProjectId) : IRequest<Result<List<ParasutProductDto>>>;
