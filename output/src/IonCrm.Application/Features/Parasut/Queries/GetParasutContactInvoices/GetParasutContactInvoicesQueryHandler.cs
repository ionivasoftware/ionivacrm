using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Parasut.Queries.GetParasutInvoices;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutContactInvoices;

/// <summary>Handles <see cref="GetParasutContactInvoicesQuery"/>.</summary>
public sealed class GetParasutContactInvoicesQueryHandler
    : IRequestHandler<GetParasutContactInvoicesQuery, Result<GetParasutInvoicesDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<GetParasutContactInvoicesQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetParasutContactInvoicesQueryHandler"/>.</summary>
    public GetParasutContactInvoicesQueryHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger<GetParasutContactInvoicesQueryHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<GetParasutInvoicesDto>> Handle(
        GetParasutContactInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
            connection, _parasutClient, _connectionRepository, _logger, cancellationToken);
        if (conn is null)
            return Result<GetParasutInvoicesDto>.Failure(tokenError!);

        try
        {
            var response = await _parasutClient.GetContactInvoicesAsync(
                conn.AccessToken!,
                conn.CompanyId,
                request.ParasutContactId,
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
                "Failed to fetch Paraşüt invoices for contact {ContactId} in project {ProjectId}.",
                request.ParasutContactId, request.ProjectId);
            return Result<GetParasutInvoicesDto>.Failure(
                $"Cari hareketleri alınamadı: {ex.Message}");
        }
    }
}
