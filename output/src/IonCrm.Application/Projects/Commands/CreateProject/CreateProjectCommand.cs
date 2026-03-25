using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Projects.Commands.CreateProject;

public record CreateProjectCommand(
    string Name,
    string? Description) : IRequest<Result<ProjectDto>>;

public record ProjectDto(Guid Id, string Name, string? Description, bool IsActive, DateTime CreatedAt);
