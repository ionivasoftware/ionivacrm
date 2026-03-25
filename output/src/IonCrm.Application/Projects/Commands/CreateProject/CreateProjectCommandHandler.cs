using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Projects.Commands.CreateProject;

public class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Result<ProjectDto>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;

    public CreateProjectCommandHandler(IProjectRepository projectRepository, ICurrentUserService currentUser)
    {
        _projectRepository = projectRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<ProjectDto>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin)
            return Result<ProjectDto>.Failure("Access denied. SuperAdmin required.");

        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true
        };

        await _projectRepository.AddAsync(project, cancellationToken);
        return Result<ProjectDto>.Success(new ProjectDto(project.Id, project.Name, project.Description, project.IsActive, project.CreatedAt));
    }
}
