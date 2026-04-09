using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<UpsertParasutProductCommandHandler> _logger;

    public UpsertParasutProductCommandHandler(
        IParasutProductRepository productRepository,
        ILogger<UpsertParasutProductCommandHandler> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<Result<ParasutProductDto>> Handle(
        UpsertParasutProductCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ParasutProduct upsert: project={ProjectId} name='{Name}' parasutId={ParasutId} unitPrice={Price} taxRate={Tax}",
            request.ProjectId, request.ProductName, request.ParasutProductId, request.UnitPrice, request.TaxRate);

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
            existing.EmsProductId       = request.EmsProductId;

            try
            {
                await _productRepository.UpdateAsync(existing, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ParasutProduct update FAILED: id={Id} project={ProjectId} name='{Name}'",
                    existing.Id, request.ProjectId, request.ProductName);
                return Result<ParasutProductDto>.Failure(
                    $"Ürün eşleştirmesi güncellenemedi: {ex.GetBaseException().Message}");
            }
            product = existing;
            _logger.LogInformation(
                "ParasutProduct UPDATED: id={Id} project={ProjectId} name='{Name}'",
                product.Id, product.ProjectId, product.ProductName);
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
                TaxRate             = request.TaxRate,
                EmsProductId        = request.EmsProductId
            };

            try
            {
                product = await _productRepository.AddAsync(newProduct, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ParasutProduct insert FAILED: project={ProjectId} name='{Name}'",
                    request.ProjectId, request.ProductName);
                return Result<ParasutProductDto>.Failure(
                    $"Ürün eşleştirmesi kaydedilemedi: {ex.GetBaseException().Message}");
            }
            _logger.LogInformation(
                "ParasutProduct INSERTED: id={Id} project={ProjectId} name='{Name}'",
                product.Id, product.ProjectId, product.ProductName);
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
            EmsProductId        = product.EmsProductId,
            CreatedAt           = product.CreatedAt,
            UpdatedAt           = product.UpdatedAt
        });
    }
}
