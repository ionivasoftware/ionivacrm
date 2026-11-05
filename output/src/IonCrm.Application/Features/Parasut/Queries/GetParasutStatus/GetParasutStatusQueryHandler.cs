using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutStatus;

/// <summary>Handles <see cref="GetParasutStatusQuery"/>.</summary>
public sealed class GetParasutStatusQueryHandler
    : IRequestHandler<GetParasutStatusQuery, Result<ParasutStatusDto>>
{
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly IParasutClient _parasutClient;

    /// <summary>Initialises a new instance of <see cref="GetParasutStatusQueryHandler"/>.</summary>
    public GetParasutStatusQueryHandler(IParasutConnectionRepository connectionRepository, IParasutClient parasutClient)
    {
        _connectionRepository = connectionRepository;
        _parasutClient = parasutClient;
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

        var canConnectWithOAuth = _parasutClient.OAuth2Available;
        if (connection is null)
        {
            return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
                IsConnected:       false,
                CompanyId:         null,
                CompanyName:       null,
                Username:          null,
                TokenExpiresAt:    null,
                LastConnectedAt:   null,
                LastError:         null,
                ReconnectAttempts: 0,
                UsesOAuth:         false,
                NeedsReconnect:    false,
                CanConnectWithOAuth: canConnectWithOAuth,
                RefreshTokenExpiresAt: null,
                ConnectedAt:       null));
        }

        var usesOAuth = connection.AuthFlow == ParasutAuthFlow.OAuth2;
        var now = DateTime.UtcNow;
        // New flow: "connected" = can make a call now or can refresh itself (live refresh token, not flagged dead).
        var isConnected = connection.IsConnected
            || (usesOAuth && !connection.NeedsReconnect && !string.IsNullOrEmpty(connection.RefreshToken)
                && (connection.RefreshTokenExpiresAt is null || connection.RefreshTokenExpiresAt > now));

        return Result<ParasutStatusDto>.Success(new ParasutStatusDto(
            IsConnected:       isConnected,
            CompanyId:         connection.CompanyId > 0 ? connection.CompanyId : null,
            CompanyName:       connection.CompanyName,
            Username:          string.IsNullOrWhiteSpace(connection.Username) ? null : connection.Username,
            TokenExpiresAt:    connection.TokenExpiresAt,
            LastConnectedAt:   connection.LastConnectedAt,
            LastError:         connection.LastError,
            ReconnectAttempts: connection.ReconnectAttempts,
            UsesOAuth:         usesOAuth,
            NeedsReconnect:    connection.NeedsReconnect,
            CanConnectWithOAuth: canConnectWithOAuth,
            RefreshTokenExpiresAt: connection.RefreshTokenExpiresAt,
            ConnectedAt:       connection.ConnectedAt ?? connection.LastConnectedAt));
    }
}
