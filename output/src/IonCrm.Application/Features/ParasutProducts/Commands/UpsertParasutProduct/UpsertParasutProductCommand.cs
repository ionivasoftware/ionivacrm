using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.UpsertParasutProduct;

/// <summary>
/// Upsert (create-or-update) a Paraşüt product configuration by name.
/// Since the 6 product names are fixed, this command either creates a new record
/// or updates the existing one for the same project + product name.
/// </summary>
public record UpsertParasutProductCommand(
    Guid ProjectId,
    string ProductName,
    string ParasutProductId,
    decimal UnitPrice,
    decimal TaxRate
) : IRequest<Result<ParasutProductDto>>;
