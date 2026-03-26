using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.API.Controllers;

/// <summary>Returns recent activity notifications for the current project.</summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public record NotificationDto(
        string Id,
        string Type,
        string Title,
        string Description,
        DateTime CreatedAt
    );

    /// <summary>Returns the last 10 activities (contact histories) across the project.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<NotificationDto>>), 200)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveProjectId = projectId ?? _currentUser.ProjectIds.FirstOrDefault();

        var histories = await _db.ContactHistories
            .Include(h => h.Customer)
            .Include(h => h.CreatedByUser)
            .Where(h => effectiveProjectId == Guid.Empty || h.ProjectId == effectiveProjectId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var notifications = histories.Select(h => new NotificationDto(
            h.Id.ToString(),
            h.Type.ToString(),
            h.Subject ?? h.Type.ToString(),
            $"{h.Customer?.CompanyName ?? "—"}" + (h.CreatedByUser != null ? $" · {h.CreatedByUser.FirstName} {h.CreatedByUser.LastName}".Trim() : ""),
            h.CreatedAt
        )).ToList();

        return Ok(ApiResponse<List<NotificationDto>>.Ok(notifications));
    }
}
