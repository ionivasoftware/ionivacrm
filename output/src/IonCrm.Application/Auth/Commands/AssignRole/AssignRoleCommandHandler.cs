using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.AssignRole;

/// <summary>
/// Handles <see cref="AssignRoleCommand"/> — upserts a UserProjectRole assignment.
/// </summary>
public class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IRepository<UserProjectRole> _roleRepository;
    private readonly IRepository<Project> _projectRepository;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="AssignRoleCommandHandler"/>.</summary>
    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRepository<UserProjectRole> roleRepository,
        IRepository<Project> projectRepository,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _projectRepository = projectRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure($"User {request.UserId} not found.");

        var project = await _projectRepository.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Project {request.ProjectId} not found.");

        // Check for existing assignment
        var existing = (await _roleRepository.FindAsync(
            r => r.UserId == request.UserId && r.ProjectId == request.ProjectId,
            cancellationToken)).FirstOrDefault();

        if (existing is not null)
        {
            existing.Role = request.Role;
            await _roleRepository.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation(
                "Updated role for User {UserId} in Project {ProjectId} to {Role}",
                request.UserId, request.ProjectId, request.Role);
        }
        else
        {
            var assignment = new UserProjectRole
            {
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                Role = request.Role
            };
            await _roleRepository.AddAsync(assignment, cancellationToken);
            _logger.LogInformation(
                "Assigned role {Role} to User {UserId} in Project {ProjectId}",
                request.Role, request.UserId, request.ProjectId);
        }

        return Result.Success();
    }
}
