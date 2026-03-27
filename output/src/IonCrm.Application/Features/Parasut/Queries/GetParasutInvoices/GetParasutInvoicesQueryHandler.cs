using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutInvoices;

/// <summary>Handles <see cref="GetParasutInvoicesQuery"/>.</summary>
public sealed class GetParasutInvoicesQueryHandler
    : IRequestHandler<GetParasutInvoicesQuery, Result<GetParasutInvoicesDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<GetParasutInvoicesQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetParasutInvoicesQueryHandler"/>.</summary>
    public GetParasutInvoicesQueryHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger<GetParasutInvoicesQueryHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<GetParasutInvoicesDto>> Handle(
        GetParasutInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null || !connection.IsConnected)
            return Result<GetParasutInvoicesDto>.Failure(
                "Paraşüt bağlantısı bulunamadı veya token süresi dolmuş.");

        try
        {
            var response = await _parasutClient.GetSalesInvoicesAsync(
                connection.AccessToken!,
                connection.CompanyId,
                request.Page,
                request.PageSize,
                cancellationToken);

            var items = response.Data.Select(d => new ParasutInvoiceItem(
                Id:              d.Id ?? string.Empty,
                IssueDate:       d.Attributes.IssueDate,
                DueDate:         d.Attributes.DueDate,
                Currency:        d.Attributes.Currency,
                GrossTotal:      d.Attributes.GrossTotal ?? 0,
                NetTotal:        d.Attributes.NetTotal ?? 0,
                TotalPaid:       d.Attributes.TotalPaid ?? 0,
                Remaining:       d.Attributes.Remaining ?? 0,
                Description:     d.Attributes.Description,
                ArchivingStatus: d.Attributes.ArchivingStatus)).ToList();

            return Result<GetParasutInvoicesDto>.Success(new GetParasutInvoicesDto(
                Items:       items,
                TotalCount:  response.Meta?.TotalCount ?? items.Count,
                TotalPages:  response.Meta?.TotalPages ?? 1,
                CurrentPage: response.Meta?.CurrentPage ?? request.Page));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch Paraşüt invoices for project {ProjectId}.", request.ProjectId);
            return Result<GetParasutInvoicesDto>.Failure(
                $"Faturalar alınamadı: {ex.Message}");
        }
    }
}
