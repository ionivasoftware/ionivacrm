using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.CreateParasutProduct;

/// <summary>
/// Handler for <see cref="CreateParasutProductCommand"/>.
/// Creates a new product configuration in the database.
/// </summary>
public class CreateParasutProductCommandHandler : IRequestHandler<CreateParasutProductCommand, Result<ParasutProductDto>>
{
    private readonly IParasutProductRepository _productRepository;

    public CreateParasutProductCommandHandler(IParasutProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ParasutProductDto>> Handle(
        CreateParasutProductCommand request,
        CancellationToken cancellationToken)
    {
        // Check if a global product with the same name already exists.
        var existing = await _productRepository.GetByNameAsync(
            request.ProductName,
            cancellationToken);

        if (existing != null)
        {
            return Result<ParasutProductDto>.Failure($"Product '{request.ProductName}' already exists.");
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return Result<ParasutProductDto>.Failure("Product name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ParasutProductId))
        {
            return Result<ParasutProductDto>.Failure("Paraşüt product ID is required.");
        }

        if (request.UnitPrice < 0)
        {
            return Result<ParasutProductDto>.Failure("Unit price cannot be negative.");
        }

        if (request.TaxRate < 0 || request.TaxRate > 1)
        {
            return Result<ParasutProductDto>.Failure("Tax rate must be between 0 and 1 (e.g., 0.20 for 20%).");
        }

        var product = new ParasutProduct
        {
            ProjectId = null, // global mapping
            ProductName = request.ProductName,
            ParasutProductId = request.ParasutProductId,
            UnitPrice = request.UnitPrice,
            TaxRate = request.TaxRate
        };

        var created = await _productRepository.AddAsync(product, cancellationToken);

        var dto = new ParasutProductDto
        {
            Id = created.Id,
            ProductName = created.ProductName,
            ParasutProductId = created.ParasutProductId,
            UnitPrice = created.UnitPrice,
            TaxRate = created.TaxRate,
            CreatedAt = created.CreatedAt,
            UpdatedAt = created.UpdatedAt
        };

        return Result<ParasutProductDto>.Success(dto);
    }
}
