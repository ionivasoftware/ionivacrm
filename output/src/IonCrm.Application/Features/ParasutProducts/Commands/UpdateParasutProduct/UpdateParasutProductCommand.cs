using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.UpdateParasutProduct;

/// <summary>
/// Command to update an existing Paraşüt product configuration.
/// Allows changing Paraşüt product ID, price, or tax rate.
/// </summary>
public record UpdateParasutProductCommand(
    Guid Id,
    string ProductName,
    string ParasutProductId,
    decimal UnitPrice,
    decimal TaxRate,
    string? ParasutProductName = null,
    string? EmsProductId = null
) : IRequest<Result<ParasutProductDto>>;
