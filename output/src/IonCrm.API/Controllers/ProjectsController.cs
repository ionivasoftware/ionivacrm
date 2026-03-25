using IonCrm.Application.Projects.Commands.CreateProject;
using IonCrm.Application.Projects.Commands.UpdateProject;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

[Route("api/v1/projects")]
public class ProjectsController : ApiControllerBase
{
    private readonly IProjectRepository _projectRepository;

    public ProjectsController(IProjectRepository projectRepository)
        => _projectRepository = projectRepository;

    /// <summary>Returns all projects (active and inactive).</summary>
    [HttpGet]
    public async Task<IActionResult> GetProjects(CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepository.GetAllAsync(cancellationToken);
        var dtos = projects.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            description = p.Description,
            isActive = p.IsActive,
            createdAt = p.CreatedAt,
            updatedAt = p.UpdatedAt
        });
        return OkResponse(dtos);
    }

    /// <summary>Creates a new project. SuperAdmin only.</summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> CreateProject(
        [FromBody] CreateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result, created: true);
    }

    /// <summary>Updates an existing project. SuperAdmin only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateProject(
        Guid id,
        [FromBody] UpdateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command with { Id = id }, cancellationToken);
        return ResultToResponse(result);
    }
}
