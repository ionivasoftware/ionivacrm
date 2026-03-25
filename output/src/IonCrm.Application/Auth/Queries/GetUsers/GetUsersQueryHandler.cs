using IonCrm.Application.Auth;
using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Queries.GetUsers;

/// <summary>
/// Handles <see cref="GetUsersQuery"/> — returns a list of users scoped to the
/// current user's project membership (or all users for SuperAdmin).
/// </summary>
public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, Result<List<UserDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetUsersQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetUsersQueryHandler"/>.</summary>
    public GetUsersQueryHandler(
        IUserRepository userRepository,
        ICurrentUserService currentUser,
        ILogger<GetUsersQueryHandler> logger)
    {
        _userRepository = userRepository;
        _currentUser    = currentUser;
        _logger         = logger;
    }

    /// <inheritdoc />
    public async Task<Result<List<UserDto>>> Handle(
        GetUsersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "GetUsers requested by UserId={UserId} ProjectFilter={ProjectId}",
            _currentUser.UserId, request.ProjectId);

        if (_currentUser.IsSuperAdmin)
        {
            // SuperAdmin: return all users or filter by the requested project
            if (request.ProjectId.HasValue)
            {
                var projectUsers = await _userRepository.GetByProjectIdAsync(
                    request.ProjectId.Value, cancellationToken);
                return Result<List<UserDto>>.Success(
                    projectUsers.Select(UserMappingHelper.MapToDto).ToList());
            }

            var allUsers = await _userRepository.GetAllAsync(cancellationToken);
            return Result<List<UserDto>>.Success(
                allUsers.Select(UserMappingHelper.MapToDto).ToList());
        }

        // Non-SuperAdmin: scoped to the first matching project
        var scopedProjectId = request.ProjectId.HasValue &&
                              _currentUser.ProjectIds.Contains(request.ProjectId.Value)
            ? request.ProjectId.Value
            : _currentUser.ProjectIds.FirstOrDefault();

        if (scopedProjectId == Guid.Empty)
            return Result<List<UserDto>>.Success(new List<UserDto>());

        var users = await _userRepository.GetByProjectIdAsync(scopedProjectId, cancellationToken);
        return Result<List<UserDto>>.Success(
            users.Select(UserMappingHelper.MapToDto).ToList());
    }
}
