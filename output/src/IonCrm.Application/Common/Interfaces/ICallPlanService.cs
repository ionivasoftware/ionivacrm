using IonCrm.Application.Features.CallPlan;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>Result of one planner run for one project.</summary>
public record CallPlanRunResult(Guid ProjectId, DateOnly PlanDate, int Created, bool AlreadyRan, string Summary);

/// <summary>Runs the CSO planner: builds candidates from CRM data, fills the daily quotas, writes appointments.</summary>
public interface ICallPlanService
{
    CallPlanQuotas Quotas { get; }

    /// <summary>Plans one project. Idempotent per local day unless <paramref name="force"/>; a forced run still only tops up missing slots.</summary>
    Task<CallPlanRunResult> RunForProjectAsync(Guid projectId, bool force, CancellationToken cancellationToken = default);

    /// <summary>Plans every project that has customers.</summary>
    Task<List<CallPlanRunResult>> RunAllAsync(bool force, CancellationToken cancellationToken = default);
}
