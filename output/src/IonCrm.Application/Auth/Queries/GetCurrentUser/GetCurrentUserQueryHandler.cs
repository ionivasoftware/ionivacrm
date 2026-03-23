using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Queries.GetCurrentUser;

/// <summary>
/// Handles <see cref="GetCurrentUserQuery"/> — loads the authenticated user from the database
/// and returns a <see cref="UserDto"/> including project-role assignments.
/// </summary>
public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCurrentUserQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCurrentUserQueryHandler"/>.</summary>
    public GetCurrentUserQueryHandler(
        IUserRepository userRepository,
        ICurrentUserService currentUser,
        ILogger<GetCurrentUserQueryHandler> logger)
    {
        _userRepository = userRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> Handle(
        GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<UserDto>.Failure("Not authenticated.");

        var user = await _userRepository.GetByIdWithRolesAsync(
            _currentUser.UserId, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "GetCurrentUser: user {UserId} found in JWT but not in database", _currentUser.UserId);
            return Result<UserDto>.Failure("User not found.");
        }

        return Result<UserDto>.Success(UserMappingHelper.MapToDto(user));
    }
}
