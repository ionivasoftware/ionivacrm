using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ILeadCandidateRepository"/>. Uses IgnoreQueryFilters: the agent endpoint is anonymous (API key) and dedupe is cross-project.</summary>
public sealed class LeadCandidateRepository : ILeadCandidateRepository
{
    private readonly ApplicationDbContext _context;

    public LeadCandidateRepository(ApplicationDbContext context) => _context = context;

    private IQueryable<LeadCandidate> Candidates => _context.LeadCandidates.IgnoreQueryFilters().Where(c => !c.IsDeleted);

    public Task<LeadCandidate?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Candidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<(List<LeadCandidate> Items, int TotalCount)> ListAsync(LeadCandidateStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var q = Candidates;
        if (status is { } s) q = q.Where(c => c.Status == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(c => EF.Functions.ILike(c.CompanyName, term) || (c.City != null && EF.Functions.ILike(c.City, term)) || (c.Phone != null && EF.Functions.ILike(c.Phone, term)));
        }
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(c => c.Status == LeadCandidateStatus.Pending).ThenByDescending(c => c.Confidence).ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<Dictionary<LeadCandidateStatus, int>> CountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Candidates.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.Count);
    }

    public Task<LeadCandidate?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        Candidates.FirstOrDefaultAsync(c => c.Fingerprint == fingerprint, cancellationToken);

    public async Task<List<(Guid Id, string Name, string NormalizedName, string? NormalizedPhone, string? Domain, string? TaxNumber, string? City)>> KnownCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Candidates.Select(c => new { c.Id, c.CompanyName, c.NormalizedName, c.NormalizedPhone, c.Domain, c.TaxNumber, c.City }).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.CompanyName, r.NormalizedName, r.NormalizedPhone, r.Domain, r.TaxNumber, r.City)).ToList();
    }

    public async Task<List<(Guid Id, string Name, string? Phone, string? Email, string? TaxNumber, string? Address)>> KnownCustomersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.Customers.IgnoreQueryFilters().Where(c => !c.IsDeleted)
            .Select(c => new { c.Id, c.CompanyName, c.Phone, c.Email, c.TaxNumber, c.Address }).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.CompanyName, r.Phone, r.Email, r.TaxNumber, r.Address)).ToList();
    }

    public async Task AddAsync(LeadCandidate candidate, CancellationToken cancellationToken = default)
    {
        await _context.LeadCandidates.AddAsync(candidate, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(LeadCandidate candidate, CancellationToken cancellationToken = default)
    {
        candidate.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(candidate).State == EntityState.Detached) _context.LeadCandidates.Update(candidate);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<LeadCandidate>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var set = ids.ToList();
        return Candidates.Where(c => set.Contains(c.Id)).ToListAsync(cancellationToken);
    }

    public Task<LeadAgentRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default) =>
        _context.LeadAgentRuns.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.RunId == runId && !r.IsDeleted, cancellationToken);

    public Task<List<LeadAgentRun>> ListRunsAsync(int limit, CancellationToken cancellationToken = default) =>
        _context.LeadAgentRuns.IgnoreQueryFilters().Where(r => !r.IsDeleted).OrderByDescending(r => r.StartedAt).Take(limit).ToListAsync(cancellationToken);

    public async Task AddRunAsync(LeadAgentRun run, CancellationToken cancellationToken = default)
    {
        await _context.LeadAgentRuns.AddAsync(run, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRunAsync(LeadAgentRun run, CancellationToken cancellationToken = default)
    {
        run.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(run).State == EntityState.Detached) _context.LeadAgentRuns.Update(run);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<HashSet<string>> RecentlyScannedCitiesAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _context.LeadAgentRuns.IgnoreQueryFilters().Where(r => !r.IsDeleted && r.StartedAt >= sinceUtc).Select(r => r.Cities).ToListAsync(cancellationToken);
        return rows.SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Guid?> ResolveDefaultProjectIdAsync(Guid? configured, CancellationToken cancellationToken = default)
    {
        var projects = _context.Projects.IgnoreQueryFilters().Where(p => !p.IsDeleted && p.IsActive);
        if (configured is { } c && c != Guid.Empty && await projects.AnyAsync(p => p.Id == c, cancellationToken)) return c;
        var liftdesk = await projects.Where(p => p.LiftdeskApiKey != null && p.LiftdeskApiKey != "").OrderBy(p => p.CreatedAt).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (liftdesk is not null) return liftdesk;
        return await projects.OrderBy(p => p.CreatedAt).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<(Guid Id, string Name)>> ProjectsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.Projects.IgnoreQueryFilters().Where(p => !p.IsDeleted && p.IsActive).OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }
}
