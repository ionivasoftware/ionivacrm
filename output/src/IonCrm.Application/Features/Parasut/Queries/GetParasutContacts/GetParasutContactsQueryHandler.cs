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

        var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
            connection, _parasutClient, _connectionRepository, _logger, cancellationToken);
        if (conn is null)
            return Result<GetParasutContactsDto>.Failure(tokenError!);

        try
        {
            bool hasSearch = !string.IsNullOrWhiteSpace(request.Search);

            // Paraşüt v4 does not support server-side name filtering.
            // When a search term is given, fetch a large batch and filter in memory.
            var response = await _parasutClient.GetContactsAsync(
                conn.AccessToken!,
                conn.CompanyId,
                page:     hasSearch ? 1 : request.Page,
                pageSize: hasSearch ? 200 : request.PageSize,
                search:   null,
                cancellationToken);

            var allItems = response.Data.Select(d => new ParasutContactItem(
                Id:          d.Id ?? string.Empty,
                Name:        d.Attributes.Name,
                Email:       d.Attributes.Email,
                Phone:       d.Attributes.Phone,
                ContactType: d.Attributes.ContactType,
                AccountType: d.Attributes.AccountType,
                TaxNumber:   d.Attributes.TaxNumber)).ToList();

            if (hasSearch)
            {
                var term = request.Search!.Trim();
                allItems = allItems
                    .Where(c => c.Name != null &&
                                c.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();

                return Result<GetParasutContactsDto>.Success(new GetParasutContactsDto(
                    Items:       allItems,
                    TotalCount:  allItems.Count,
                    TotalPages:  1,
                    CurrentPage: 1));
            }

            return Result<GetParasutContactsDto>.Success(new GetParasutContactsDto(
                Items:       allItems,
                TotalCount:  response.Meta?.TotalCount ?? allItems.Count,
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
