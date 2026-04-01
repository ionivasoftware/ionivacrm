using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.CreateParasutProduct;

/// <summary>
/// Command to create a new Paraşüt product configuration.
/// Maps a CRM product (e.g., "1 Aylık Üyelik") to Paraşüt product ID, price, and tax rate.
/// </summary>
public record CreateParasutProductCommand(
    Guid ProjectId,
    string ProductName,
    string ParasutProductId,
    decimal UnitPrice,
    decimal TaxRate
) : IRequest<Result<ParasutProductDto>>;
