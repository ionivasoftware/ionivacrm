using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutStatus;

/// <summary>Handles <see cref="GetParasutStatusQuery"/>.</summary>
public sealed class GetParasutStatusQueryHandler
    : IRequestHandler<GetParasutStatusQuery, Result<ParasutStatusDto>>
{
    private readonly IParasutConnectionRepository _connectionRepository;

    /// <summary>Initialises a new instance of <see cref="GetParasutStatusQueryHandler"/>.</summary>
    public GetParasutStatusQueryHandler(IParasutConnectionRepository connectionRepository)
    {
        _connectionRepository = connectionRepository;
    }

    /// <inheritdoc />
    public async Task<Result<ParasutStatusDto>> Handle(
        GetParasutStatusQuery request,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null)
        {
            return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
                IsConnected:    false,
                CompanyId:      null,
                Username:       null,
                TokenExpiresAt: null));
        }

        return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
            IsConnected:    connection.IsConnected,
            CompanyId:      connection.CompanyId,
            Username:       connection.Username,
            TokenExpiresAt: connection.TokenExpiresAt));
    }
}
