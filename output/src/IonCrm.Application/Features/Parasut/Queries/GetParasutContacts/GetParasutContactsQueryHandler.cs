using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutContacts;

/// <summary>Handles <see cref="GetParasutContactsQuery"/>.</summary>
public sealed class GetParasutContactsQueryHandler
    : IRequestHandler<GetParasutContactsQuery, Result<GetParasutContactsDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<GetParasutContactsQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetParasutContactsQueryHandler"/>.</summary>
    public GetParasutContactsQueryHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger<GetParasutContactsQueryHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<GetParasutContactsDto>> Handle(
        GetParasutContactsQuery request,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null || !connection.IsConnected)
            return Result<GetParasutContactsDto>.Failure(
                "Paraşüt bağlantısı bulunamadı veya token süresi dolmuş.");

        try
        {
            var response = await _parasutClient.GetContactsAsync(
                connection.AccessToken!,
                connection.CompanyId,
                request.Page,
                request.PageSize,
                cancellationToken);

            var items = response.Data.Select(d => new ParasutContactItem(
                Id:          d.Id ?? string.Empty,
                Name:        d.Attributes.Name,
                Email:       d.Attributes.Email,
                Phone:       d.Attributes.Phone,
                ContactType: d.Attributes.ContactType,
                AccountType: d.Attributes.AccountType,
                TaxNumber:   d.Attributes.TaxNumber)).ToList();

            return Result<GetParasutContactsDto>.Success(new GetParasutContactsDto(
                Items:       items,
                TotalCount:  response.Meta?.TotalCount ?? items.Count,
                TotalPages:  response.Meta?.TotalPages ?? 1,
                CurrentPage: response.Meta?.CurrentPage ?? request.Page));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch Paraşüt contacts for project {ProjectId}.", request.ProjectId);
            return Result<GetParasutContactsDto>.Failure(
                $"Cariler alınamadı: {ex.Message}");
        }
    }
}
