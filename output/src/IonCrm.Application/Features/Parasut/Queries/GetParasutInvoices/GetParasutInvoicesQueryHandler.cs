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
        var connection = await _connectionRepository.GetEffectiveConnectionAsync(
            request.ProjectId, cancellationToken);

        var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
            connection, _parasutClient, _connectionRepository, _logger, cancellationToken);
        if (conn is null)
            return Result<GetParasutInvoicesDto>.Failure(tokenError!);

        try
        {
            var response = await _parasutClient.GetSalesInvoicesAsync(
                conn.AccessToken!,
                conn.CompanyId,
                request.Page,
                request.PageSize,
                cancellationToken);

            static decimal Parse(string? s) =>
                decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;

            var items = response.Data.Select(d => new ParasutInvoiceItem(
                Id:              d.Id ?? string.Empty,
                IssueDate:       d.Attributes.IssueDate,
                DueDate:         d.Attributes.DueDate,
                Currency:        d.Attributes.Currency,
                GrossTotal:      Parse(d.Attributes.GrossTotal),
                NetTotal:        Parse(d.Attributes.NetTotal),
                TotalPaid:       Parse(d.Attributes.TotalPaid),
                Remaining:       Parse(d.Attributes.Remaining),
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
