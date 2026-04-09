using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Queries.GetParasutProducts;

/// <summary>
/// Query to retrieve the global Paraşüt product catalog. Mappings are project-independent —
/// one global catalog shared by all projects, mirroring the global Paraşüt connection.
/// </summary>
public record GetParasutProductsQuery : IRequest<Result<List<ParasutProductDto>>>;
