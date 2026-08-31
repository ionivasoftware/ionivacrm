using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.RemoveRole;

/// <summary>Handles <see cref="RemoveRoleCommand"/> — soft-deletes a user's role in a project.</summary>
public class RemoveRoleCommandHandler : IRequestHandler<RemoveRoleCommand, Result>
{
    private readonly IRepository<UserProjectRole> _roleRepository;
    private readonly ILogger<RemoveRoleCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="RemoveRoleCommandHandler"/>.</summary>
    public RemoveRoleCommandHandler(
        IRepository<UserProjectRole> roleRepository,
        ILogger<RemoveRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(RemoveRoleCommand request, CancellationToken cancellationToken)
    {
        var existing = (await _roleRepository.FindAsync(
            r => r.UserId == request.UserId && r.ProjectId == request.ProjectId && !r.IsDeleted,
            cancellationToken)).FirstOrDefault();

        if (existing is null)
            return Result.Failure(
                $"User {request.UserId} has no active role in project {request.ProjectId}.");

        existing.IsDeleted = true;
        await _roleRepository.UpdateAsync(existing, cancellationToken);

        _logger.LogInformation(
            "Removed role for User {UserId} in Project {ProjectId}.",
            request.UserId, request.ProjectId);

        return Result.Success();
    }
}
