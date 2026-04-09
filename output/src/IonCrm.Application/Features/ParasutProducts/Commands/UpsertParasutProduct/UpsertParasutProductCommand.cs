using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.UpsertParasutProduct;

/// <summary>
/// Upsert (create-or-update) a global Paraşüt product mapping by name.
/// The product catalog is project-independent — there is one global mapping per ProductName
/// shared by all projects, mirroring the global Paraşüt connection.
/// </summary>
public record UpsertParasutProductCommand(
    string ProductName,
    string ParasutProductId,
    decimal UnitPrice,
    decimal TaxRate,
    string? ParasutProductName = null,
    string? EmsProductId = null
) : IRequest<Result<ParasutProductDto>>;
