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
///
/// When <c>ProjectId</c> is <c>null</c>, the connection is stored as the global connection
/// (shared by all projects). When <c>ProjectId</c> has a value, a project-specific
/// connection is upserted without touching the global one.
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
        var target = request.ProjectId.HasValue
            ? $"project {request.ProjectId}"
            : "global";

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

            // 2. Upsert connection record — use strict lookup (no fallback) so project-specific
            //    and global connections are managed independently.
            ParasutConnection? existing = request.ProjectId.HasValue
                ? await _connectionRepository.GetByProjectIdAsync(request.ProjectId.Value, cancellationToken)
                : await _connectionRepository.GetGlobalAsync(cancellationToken);

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
                existing.LastError      = null;
                existing.ReconnectAttempts = 0;
                await _connectionRepository.UpdateAsync(existing, cancellationToken);
            }
            else
            {
                var connection = new ParasutConnection
                {
                    ProjectId      = request.ProjectId,   // null → global
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
                "Paraşüt connected for {Target}. Company={CompanyId} TokenExpiry={Expiry:u}",
                target, request.CompanyId, expiresAt);

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
                "Failed to connect Paraşüt for {Target}.", target);
            return Result<ConnectParasutDto>.Failure($"Paraşüt bağlantısı kurulamadı: {ex.Message}");
        }
    }
}
