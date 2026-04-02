using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Commands.UpsertParasutProduct;

/// <summary>
/// Handler for <see cref="UpsertParasutProductCommand"/>.
/// Creates a new product or updates the existing one with the same project + product name.
/// Designed for the 6 fixed product catalog: memberships (1-month, 1-year) + SMS packages.
/// </summary>
public class UpsertParasutProductCommandHandler
    : IRequestHandler<UpsertParasutProductCommand, Result<ParasutProductDto>>
{
    private readonly IParasutProductRepository _productRepository;

    public UpsertParasutProductCommandHandler(IParasutProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ParasutProductDto>> Handle(
        UpsertParasutProductCommand request,
        CancellationToken cancellationToken)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.ProductName))
            return Result<ParasutProductDto>.Failure("Ürün adı zorunludur.");

        if (string.IsNullOrWhiteSpace(request.ParasutProductId))
            return Result<ParasutProductDto>.Failure("Paraşüt ürün ID'si zorunludur.");

        if (request.UnitPrice < 0)
            return Result<ParasutProductDto>.Failure("Birim fiyat negatif olamaz.");

        if (request.TaxRate < 0 || request.TaxRate > 1)
            return Result<ParasutProductDto>.Failure("KDV oranı 0 ile 1 arasında olmalıdır (örn. 0.20 = %20).");

        // Try to find existing record for this project + product name
        var existing = await _productRepository.GetByNameAsync(
            request.ProjectId, request.ProductName, cancellationToken);

        ParasutProduct product;

        if (existing is not null)
        {
            // Update existing
            existing.ParasutProductId   = request.ParasutProductId;
            existing.ParasutProductName = request.ParasutProductName;
            existing.UnitPrice          = request.UnitPrice;
            existing.TaxRate            = request.TaxRate;

            await _productRepository.UpdateAsync(existing, cancellationToken);
            product = existing;
        }
        else
        {
            // Create new
            var newProduct = new ParasutProduct
            {
                ProjectId           = request.ProjectId,
                ProductName         = request.ProductName,
                ParasutProductId    = request.ParasutProductId,
                ParasutProductName  = request.ParasutProductName,
                UnitPrice           = request.UnitPrice,
                TaxRate             = request.TaxRate
            };

            product = await _productRepository.AddAsync(newProduct, cancellationToken);
        }

        return Result<ParasutProductDto>.Success(new ParasutProductDto
        {
            Id                  = product.Id,
            ProjectId           = product.ProjectId,
            ProductName         = product.ProductName,
            ParasutProductId    = product.ParasutProductId,
            ParasutProductName  = product.ParasutProductName,
            UnitPrice           = product.UnitPrice,
            TaxRate             = product.TaxRate,
            CreatedAt           = product.CreatedAt,
            UpdatedAt           = product.UpdatedAt
        });
    }
}
