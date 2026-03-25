using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="Opportunity"/>.</summary>
public interface IOpportunityRepository : IRepository<Opportunity>
{
    /// <summary>Gets a paged list of opportunities for a specific customer.</summary>
    Task<(IReadOnlyList<Opportunity> Items, int TotalCount)> GetPagedByCustomerIdAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of opportunities across a project with optional stage filter.</summary>
    Task<(IReadOnlyList<Opportunity> Items, int TotalCount)> GetPagedByProjectAsync(
        Guid projectId,
        OpportunityStage? stage,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
