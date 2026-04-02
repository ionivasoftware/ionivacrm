using IonCrm.Application.Common.Models;
using IonCrm.Application.Projects.Commands.CreateProject;
using MediatR;

namespace IonCrm.Application.Projects.Commands.UpdateProject;

public record UpdateProjectCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    string? EmsBaseUrl = null,
    string? EmsApiKey = null,
    string? RezervAlBaseUrl = null,
    string? RezervAlApiKey = null) : IRequest<Result<ProjectDto>>;
