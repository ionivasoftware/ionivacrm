using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Handles <see cref="RefreshTokenCommand"/> — validates the refresh token,
/// revokes it, and issues a new token pair.
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponseDto>>
{
    private readonly ITokenService _tokenService;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="RefreshTokenCommandHandler"/>.</summary>
    public RefreshTokenCommandHandler(
        ITokenService tokenService,
        IUserRepository userRepository,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _tokenService = tokenService;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponseDto>> Handle(
        RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var existingToken = await _tokenService.GetActiveRefreshTokenAsync(
            request.Token, cancellationToken);

        if (existingToken is null)
        {
            _logger.LogWarning("Invalid or expired refresh token presented");
            return Result<AuthResponseDto>.Failure("Invalid or expired refresh token.");
        }

        var user = await _userRepository.GetByIdWithRolesAsync(
            existingToken.UserId, cancellationToken);

        if (user is null || !user.IsActive)
            return Result<AuthResponseDto>.Failure("User account not found or deactivated.");

        // Revoke the consumed token (one-time-use semantics)
        await _tokenService.RevokeRefreshTokenAsync(request.Token, cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user);
        var (rawNewRefreshToken, _) = await _tokenService.CreateRefreshTokenAsync(user, cancellationToken);

        _logger.LogInformation("Refresh token rotated for User {UserId}", user.Id);

        return Result<AuthResponseDto>.Success(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = rawNewRefreshToken,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(15),
            User = UserMappingHelper.MapToDto(user)
        });
    }
}
