using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;

/// <summary>
/// Handles <see cref="DisconnectParasutCommand"/>. On the new flow the refresh token is revoked at Paraşüt first (best effort),
/// so the firm's "Uygulama Entegrasyonları" list shows no leftover; tokens are then cleared and the row soft-deleted.
/// </summary>
public sealed class DisconnectParasutCommandHandler
    : IRequestHandler<DisconnectParasutCommand, Result>
{
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly IParasutClient _parasutClient;
    private readonly ITokenProtector _tokenProtector;
    private readonly ILogger<DisconnectParasutCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="DisconnectParasutCommandHandler"/>.</summary>
    public DisconnectParasutCommandHandler(
        IParasutConnectionRepository connectionRepository,
        IParasutClient parasutClient,
        ITokenProtector tokenProtector,
        ILogger<DisconnectParasutCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _parasutClient = parasutClient;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        DisconnectParasutCommand request,
        CancellationToken cancellationToken)
    {
        // Strict lookup — do not use GetEffectiveConnectionAsync here to avoid accidentally
        // soft-deleting the global connection when disconnecting a project.
        var connection = request.ProjectId.HasValue
            ? await _connectionRepository.GetByProjectIdAsync(request.ProjectId.Value, cancellationToken)
            : await _connectionRepository.GetGlobalAsync(cancellationToken);

        if (connection is null)
        {
            var notFound = request.ProjectId.HasValue
                ? "Bu proje için Paraşüt bağlantısı bulunamadı."
                : "Global Paraşüt bağlantısı bulunamadı.";
            return Result.Failure(notFound);
        }

        if (connection.AuthFlow == ParasutAuthFlow.OAuth2)
        {
            var refreshToken = ParasutOAuthFlow.ReadSecret(_tokenProtector, connection.RefreshToken);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var revoked = await _parasutClient.RevokeOAuth2TokenAsync(refreshToken, cancellationToken);
                if (!revoked) _logger.LogWarning("Paraşüt belirteci iptal edilemedi; yerel bağlantı yine de kesiliyor.");
            }
        }

        // Tokens never outlive the connection row.
        connection.AccessToken = null;
        connection.RefreshToken = null;
        connection.TokenExpiresAt = null;
        connection.RefreshTokenExpiresAt = null;
        connection.NeedsReconnect = false;
        connection.OAuthStateHash = null;
        connection.OAuthStateExpiresAt = null;
        connection.OAuthStateUserId = null;
        connection.OAuthCodeVerifier = null;
        connection.UpdatedAt = DateTime.UtcNow;
        await _connectionRepository.DeleteAsync(connection, cancellationToken);

        var target = request.ProjectId.HasValue
            ? $"project {request.ProjectId}"
            : "global";

        _logger.LogInformation(
            "Paraşüt disconnected for {Target}.", target);

        return Result.Success();
    }
}
