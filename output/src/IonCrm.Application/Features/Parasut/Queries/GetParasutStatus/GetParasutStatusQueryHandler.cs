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
        // No projectId → query the global connection directly.
        // With projectId → effective lookup (project-specific first, then fallback to global).
        var connection = request.ProjectId.HasValue
            ? await _connectionRepository.GetEffectiveConnectionAsync(request.ProjectId.Value, cancellationToken)
            : await _connectionRepository.GetGlobalAsync(cancellationToken);

        if (connection is null)
        {
            return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
                IsConnected:       false,
                CompanyId:         null,
                Username:          null,
                TokenExpiresAt:    null,
                LastConnectedAt:   null,
                LastError:         null,
                ReconnectAttempts: 0));
        }

        return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
            IsConnected:       connection.IsConnected,
            CompanyId:         connection.CompanyId,
            Username:          connection.Username,
            TokenExpiresAt:    connection.TokenExpiresAt,
            LastConnectedAt:   connection.LastConnectedAt,
            LastError:         connection.LastError,
            ReconnectAttempts: connection.ReconnectAttempts));
    }
}
