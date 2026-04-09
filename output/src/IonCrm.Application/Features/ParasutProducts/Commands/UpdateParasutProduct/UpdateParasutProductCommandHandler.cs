using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.UpdateParasutProduct;

/// <summary>
/// Handler for <see cref="UpdateParasutProductCommand"/>.
/// Updates an existing product configuration.
/// </summary>
public class UpdateParasutProductCommandHandler : IRequestHandler<UpdateParasutProductCommand, Result<ParasutProductDto>>
{
    private readonly IParasutProductRepository _productRepository;

    public UpdateParasutProductCommandHandler(IParasutProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ParasutProductDto>> Handle(
        UpdateParasutProductCommand request,
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.Id, cancellationToken);

        if (product == null)
        {
            return Result<ParasutProductDto>.Failure("Product not found.");
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

        // Check if renaming to a name that another global mapping already uses
        if (product.ProductName != request.ProductName)
        {
            var existing = await _productRepository.GetByNameAsync(
                request.ProductName,
                cancellationToken);

            if (existing != null && existing.Id != product.Id)
            {
                return Result<ParasutProductDto>.Failure($"Product '{request.ProductName}' already exists.");
            }
        }

        product.ProductName = request.ProductName;
        product.ParasutProductId = request.ParasutProductId;
        product.UnitPrice = request.UnitPrice;
        product.TaxRate = request.TaxRate;

        await _productRepository.UpdateAsync(product, cancellationToken);

        var dto = new ParasutProductDto
        {
            Id = product.Id,
            ProductName = product.ProductName,
            ParasutProductId = product.ParasutProductId,
            UnitPrice = product.UnitPrice,
            TaxRate = product.TaxRate,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };

        return Result<ParasutProductDto>.Success(dto);
    }
}
