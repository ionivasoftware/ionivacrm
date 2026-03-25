using IonCrm.Application.Opportunities.Commands.UpdateOpportunityStage;
using IonCrm.Application.Opportunities.Queries.GetAllProjectOpportunities;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Project-level pipeline (opportunity) endpoints.
/// Route: /api/v1/pipeline
/// </summary>
[Route("api/v1/pipeline")]
public class PipelineController : ApiControllerBase
{
    /// <summary>Gets all opportunities for a project, optionally filtered by stage.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid projectId,
        [FromQuery] OpportunityStage? stage = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllProjectOpportunitiesQuery
        {
            ProjectId = projectId,
            Stage = stage,
            Page = page,
            PageSize = pageSize,
        };
        var result = await Mediator.Send(query, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Updates the stage of an opportunity (Kanban move). </summary>
    [HttpPatch("{id:guid}/stage")]
    public async Task<IActionResult> UpdateStage(
        Guid id,
        [FromBody] UpdateOpportunityStageRequest body,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateOpportunityStageCommand { Id = id, Stage = body.Stage };
        var result = await Mediator.Send(command, cancellationToken);
        return ResultToResponse(result);
    }
}

public record UpdateOpportunityStageRequest(OpportunityStage Stage);
