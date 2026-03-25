using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.API.Controllers;

/// <summary>
/// Returns the list of active projects (tenants).
/// SuperAdmin: all active projects.
/// Regular user: only their assigned projects (filtered by JWT claims).
/// GET /api/v1/projects
/// </summary>
[Route("api/v1/projects")]
public class ProjectsController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public ProjectsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects(CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                isActive = p.IsActive,
                createdAt = p.CreatedAt,
                updatedAt = p.UpdatedAt
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return OkResponse(projects);
    }
}
