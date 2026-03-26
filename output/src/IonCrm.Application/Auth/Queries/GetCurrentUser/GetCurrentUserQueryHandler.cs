using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Queries.GetCurrentUser;

/// <summary>
/// Handles <see cref="GetCurrentUserQuery"/> — loads the authenticated user from the database,
/// issues a fresh access token with up-to-date projectIds, and returns both.
/// The fresh token guarantees that subsequent API calls pass the global query filter
/// even when the caller's current JWT was issued before roles were assigned.
/// </summary>
public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<AuthResponseDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ITokenService _tokenService;
    private readonly ILogger<GetCurrentUserQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCurrentUserQueryHandler"/>.</summary>
    public GetCurrentUserQueryHandler(
        IUserRepository userRepository,
        ICurrentUserService currentUser,
        ITokenService tokenService,
        ILogger<GetCurrentUserQueryHandler> logger)
    {
        _userRepository = userRepository;
        _currentUser = currentUser;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponseDto>> Handle(
        GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<AuthResponseDto>.Failure("Not authenticated.");

        var user = await _userRepository.GetByIdWithRolesAsync(
            _currentUser.UserId, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "GetCurrentUser: user {UserId} found in JWT but not in database", _currentUser.UserId);
            return Result<AuthResponseDto>.Failure("User not found.");
        }

        // Issue a fresh access token so the caller immediately has correct projectIds in JWT.
        // This is critical when initializeAuth is called with a stale token (e.g. roles were
        // assigned after the previous login — old JWT has projectIds=[] but DB has roles).
        var freshAccessToken = _tokenService.GenerateAccessToken(user);

        return Result<AuthResponseDto>.Success(new AuthResponseDto
        {
            AccessToken = freshAccessToken,
            RefreshToken = string.Empty, // Not rotated here — only on explicit /auth/refresh
            AccessTokenExpiresAt = _tokenService.GetAccessTokenExpiresAt(),
            User = UserMappingHelper.MapToDto(user)
        });
    }
}
