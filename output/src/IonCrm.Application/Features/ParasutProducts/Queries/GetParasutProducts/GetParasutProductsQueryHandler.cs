using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.ParasutProducts.Queries.GetParasutProducts;

/// <summary>
/// Handler for <see cref="GetParasutProductsQuery"/>.
/// Returns all products configured for the given project.
/// </summary>
public class GetParasutProductsQueryHandler : IRequestHandler<GetParasutProductsQuery, Result<List<ParasutProductDto>>>
{
    private readonly IParasutProductRepository _productRepository;

    public GetParasutProductsQueryHandler(IParasutProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<List<ParasutProductDto>>> Handle(
        GetParasutProductsQuery request,
        CancellationToken cancellationToken)
    {
        var products = await _productRepository.GetAllAsync(cancellationToken);

        var dtos = products.Select(p => new ParasutProductDto
        {
            Id                 = p.Id,
            ProductName        = p.ProductName,
            ParasutProductId   = p.ParasutProductId,
            ParasutProductName = p.ParasutProductName,
            UnitPrice          = p.UnitPrice,
            TaxRate            = p.TaxRate,
            EmsProductId       = p.EmsProductId,
            CreatedAt          = p.CreatedAt,
            UpdatedAt          = p.UpdatedAt
        }).ToList();

        return Result<List<ParasutProductDto>>.Success(dtos);
    }
}
