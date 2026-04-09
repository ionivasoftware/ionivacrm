using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.CreateParasutProduct;

/// <summary>
/// Command to create a new global Paraşüt product mapping.
/// The product catalog is project-independent — one mapping per ProductName shared by all
/// projects, mirroring the global Paraşüt connection.
/// </summary>
public record CreateParasutProductCommand(
    string ProductName,
    string ParasutProductId,
    decimal UnitPrice,
    decimal TaxRate
) : IRequest<Result<ParasutProductDto>>;
