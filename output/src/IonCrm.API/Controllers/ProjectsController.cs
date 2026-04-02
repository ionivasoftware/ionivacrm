using IonCrm.Application.Projects.Commands.CreateProject;
using IonCrm.Application.Projects.Commands.SetProjectApiKeys;
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

    /// <summary>Returns all projects (active and inactive). SuperAdmin only.</summary>
    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
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
            updatedAt = p.UpdatedAt,
            emsBaseUrl = p.EmsBaseUrl,
            emsApiKey = p.EmsApiKey,
            rezervAlBaseUrl = p.RezervAlBaseUrl,
            rezervAlApiKey = p.RezervAlApiKey,
            smsCount = p.SmsCount
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

    /// <summary>Sets EMS and Rezerval API keys for a project. SuperAdmin only.</summary>
    [HttpPut("{id:guid}/api-keys")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> SetApiKeys(
        Guid id,
        [FromBody] SetProjectApiKeysCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(command with { Id = id }, cancellationToken);
        return ResultToResponse(result);
    }
}
