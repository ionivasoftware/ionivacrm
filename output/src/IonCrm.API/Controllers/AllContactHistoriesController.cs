using IonCrm.Application.ContactHistory.Queries.GetAllContactHistories;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Global contact history endpoint — returns all contact history entries across all
/// accessible customers in the user's projects.
/// Used by the "All Conversations" / "Tüm görüşmeler" page.
/// GET /api/v1/contact-histories — paginated, filterable by project/customer/type/date
/// </summary>
[Route("api/v1/contact-histories")]
public class AllContactHistoriesController : ApiControllerBase
{
    /// <summary>
    /// Gets a paginated list of all contact history records across accessible projects.
    /// Results are ordered by ContactedAt descending (most recent first).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? customerId,
        [FromQuery] ContactType? type,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllContactHistoriesQuery
        {
            ProjectId = projectId,
            CustomerId = customerId,
            Type = type,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize
        };

        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }
}
