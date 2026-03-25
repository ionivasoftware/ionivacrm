using IonCrm.Application.Tasks.Queries.GetAllProjectTasks;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Endpoints for cross-customer project-level task queries.
/// Route: /api/v1/tasks
/// </summary>
[Route("api/v1/tasks")]
public class TasksController : ApiControllerBase
{
    /// <summary>Gets a paged list of all tasks in a project with optional filters.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAllProjectTasks(
        [FromQuery] Guid projectId,
        [FromQuery] IonCrm.Domain.Enums.TaskStatus? status = null,
        [FromQuery] TaskPriority? priority = null,
        [FromQuery] Guid? assignedUserId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllProjectTasksQuery
        {
            ProjectId = projectId,
            Status = status,
            Priority = priority,
            AssignedUserId = assignedUserId,
            Page = page,
            PageSize = pageSize
        };
        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }
}
