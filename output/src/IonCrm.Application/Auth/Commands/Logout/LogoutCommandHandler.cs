using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.Logout;

/// <summary>
/// Handles <see cref="LogoutCommand"/> — revokes one or all refresh tokens for the user.
/// </summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<LogoutCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="LogoutCommandHandler"/>.</summary>
    public LogoutCommandHandler(ITokenService tokenService, ILogger<LogoutCommandHandler> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (request.LogoutEverywhere)
        {
            await _tokenService.RevokeAllUserRefreshTokensAsync(request.UserId, cancellationToken);
            _logger.LogInformation("All sessions revoked for User {UserId}", request.UserId);
        }
        else
        {
            await _tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
            _logger.LogInformation("Single session revoked for User {UserId}", request.UserId);
        }

        return Result.Success();
    }
}
