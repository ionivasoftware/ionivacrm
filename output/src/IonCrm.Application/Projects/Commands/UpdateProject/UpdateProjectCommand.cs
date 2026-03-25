using IonCrm.Application.Common.Models;
using IonCrm.Application.Projects.Commands.CreateProject;
using MediatR;

namespace IonCrm.Application.Projects.Commands.UpdateProject;

public record UpdateProjectCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive) : IRequest<Result<ProjectDto>>;
