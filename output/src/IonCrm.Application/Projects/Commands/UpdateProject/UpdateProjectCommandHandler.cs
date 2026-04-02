using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Projects.Commands.CreateProject;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Projects.Commands.UpdateProject;

public class UpdateProjectCommandHandler : IRequestHandler<UpdateProjectCommand, Result<ProjectDto>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;

    public UpdateProjectCommandHandler(IProjectRepository projectRepository, ICurrentUserService currentUser)
    {
        _projectRepository = projectRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<ProjectDto>> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin)
            return Result<ProjectDto>.Failure("Access denied. SuperAdmin required.");

        var project = await _projectRepository.GetByIdAsync(request.Id, cancellationToken);
        if (project is null)
            return Result<ProjectDto>.Failure("Project not found.");

        project.Name        = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.IsActive    = request.IsActive;

        if (request.EmsBaseUrl is not null)
            project.EmsBaseUrl    = string.IsNullOrWhiteSpace(request.EmsBaseUrl)    ? null : request.EmsBaseUrl.Trim();
        if (request.EmsApiKey is not null)
            project.EmsApiKey     = string.IsNullOrWhiteSpace(request.EmsApiKey)     ? null : request.EmsApiKey.Trim();
        if (request.RezervAlBaseUrl is not null)
            project.RezervAlBaseUrl = string.IsNullOrWhiteSpace(request.RezervAlBaseUrl) ? null : request.RezervAlBaseUrl.Trim();
        if (request.RezervAlApiKey is not null)
            project.RezervAlApiKey  = string.IsNullOrWhiteSpace(request.RezervAlApiKey)  ? null : request.RezervAlApiKey.Trim();

        await _projectRepository.UpdateAsync(project, cancellationToken);
        return Result<ProjectDto>.Success(new ProjectDto(
            project.Id, project.Name, project.Description, project.IsActive, project.CreatedAt,
            project.EmsBaseUrl, project.EmsApiKey, project.RezervAlBaseUrl, project.RezervAlApiKey));
    }
}
