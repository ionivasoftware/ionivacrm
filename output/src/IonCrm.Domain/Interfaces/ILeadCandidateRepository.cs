using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Lead-agent candidates and runs. Global (not tenant-scoped): SuperAdmin-only screen.</summary>
public interface ILeadCandidateRepository
{
    Task<LeadCandidate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(List<LeadCandidate> Items, int TotalCount)> ListAsync(LeadCandidateStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Dictionary<LeadCandidateStatus, int>> CountsAsync(CancellationToken cancellationToken = default);

    Task<LeadCandidate?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>All candidates in matching form (any status) — rejected ones must still block re-proposal.</summary>
    Task<List<(Guid Id, string Name, string NormalizedName, string? NormalizedPhone, string? Domain, string? TaxNumber, string? City)>> KnownCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Every customer across projects (dedupe is global).</summary>
    Task<List<(Guid Id, string Name, string? Phone, string? Email, string? TaxNumber, string? Address)>> KnownCustomersAsync(CancellationToken cancellationToken = default);

    Task AddAsync(LeadCandidate candidate, CancellationToken cancellationToken = default);

    Task UpdateAsync(LeadCandidate candidate, CancellationToken cancellationToken = default);

    Task<List<LeadCandidate>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<LeadAgentRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<List<LeadAgentRun>> ListRunsAsync(int limit, CancellationToken cancellationToken = default);

    Task AddRunAsync(LeadAgentRun run, CancellationToken cancellationToken = default);

    Task UpdateRunAsync(LeadAgentRun run, CancellationToken cancellationToken = default);

    /// <summary>Provinces scanned by runs started after <paramref name="sinceUtc"/>.</summary>
    Task<HashSet<string>> RecentlyScannedCitiesAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Default target project: config id → project with Liftdesk key → first active project.</summary>
    Task<Guid?> ResolveDefaultProjectIdAsync(Guid? configured, CancellationToken cancellationToken = default);

    Task<List<(Guid Id, string Name)>> ProjectsAsync(CancellationToken cancellationToken = default);
}
