using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.ConnectParasut;

/// <summary>
/// Handles <see cref="ConnectParasutCommand"/>.
/// Authenticates with Paraşüt OAuth, persists tokens, upserts the connection record.
/// </summary>
public sealed class ConnectParasutCommandHandler
    : IRequestHandler<ConnectParasutCommand, Result<ConnectParasutDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<ConnectParasutCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="ConnectParasutCommandHandler"/>.</summary>
    public ConnectParasutCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger<ConnectParasutCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ConnectParasutDto>> Handle(
        ConnectParasutCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Authenticate with Paraşüt
            var tokenResponse = await _parasutClient.GetTokenAsync(
                new ParasutTokenRequest(
                    GrantType:    "password",
                    ClientId:     request.ClientId,
                    ClientSecret: request.ClientSecret,
                    Username:     request.Username,
                    Password:     request.Password),
                cancellationToken);

            var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // 60s buffer

            // 2. Upsert connection record
            var existing = await _connectionRepository.GetByProjectIdAsync(
                request.ProjectId, cancellationToken);

            if (existing is not null)
            {
                existing.CompanyId      = request.CompanyId;
                existing.ClientId       = request.ClientId;
                existing.ClientSecret   = request.ClientSecret;
                existing.Username       = request.Username;
                existing.Password       = request.Password;
                existing.AccessToken    = tokenResponse.AccessToken;
                existing.RefreshToken   = tokenResponse.RefreshToken;
                existing.TokenExpiresAt = expiresAt;
                await _connectionRepository.UpdateAsync(existing, cancellationToken);
            }
            else
            {
                var connection = new ParasutConnection
                {
                    ProjectId      = request.ProjectId,
                    CompanyId      = request.CompanyId,
                    ClientId       = request.ClientId,
                    ClientSecret   = request.ClientSecret,
                    Username       = request.Username,
                    Password       = request.Password,
                    AccessToken    = tokenResponse.AccessToken,
                    RefreshToken   = tokenResponse.RefreshToken,
                    TokenExpiresAt = expiresAt
                };
                await _connectionRepository.AddAsync(connection, cancellationToken);
            }

            _logger.LogInformation(
                "Paraşüt connected for project {ProjectId}. Company={CompanyId} TokenExpiry={Expiry:u}",
                request.ProjectId, request.CompanyId, expiresAt);

            return Result<ConnectParasutDto>.Success(new ConnectParasutDto(
                ProjectId:      request.ProjectId,
                CompanyId:      request.CompanyId,
                Username:       request.Username,
                IsConnected:    true,
                TokenExpiresAt: expiresAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to connect Paraşüt for project {ProjectId}.", request.ProjectId);
            return Result<ConnectParasutDto>.Failure($"Paraşüt bağlantısı kurulamadı: {ex.Message}");
        }
    }
}
