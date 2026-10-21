using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IGlobalLeadRepository"/> (IgnoreQueryFilters: background + API-key paths).</summary>
public sealed class GlobalLeadRepository : IGlobalLeadRepository
{
    private readonly ApplicationDbContext _context;

    public GlobalLeadRepository(ApplicationDbContext context) => _context = context;

    private IQueryable<GlobalLead> Leads => _context.GlobalLeads.IgnoreQueryFilters().Where(l => !l.IsDeleted);
    private IQueryable<GlobalLeadEmail> Emails => _context.GlobalLeadEmails.IgnoreQueryFilters().Where(e => !e.IsDeleted);

    public Task<GlobalLead?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<GlobalLead?> GetByUnsubscribeTokenAsync(string token, CancellationToken cancellationToken = default) =>
        Leads.FirstOrDefaultAsync(l => l.UnsubscribeToken == token, cancellationToken);

    public async Task<(List<GlobalLead> Items, int TotalCount)> ListAsync(GlobalLeadStatus? status, OutreachStatus? outreach, string? country, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var q = Leads;
        if (status is { } s) q = q.Where(l => l.Status == s);
        if (outreach is { } o) q = q.Where(l => l.OutreachStatus == o);
        if (!string.IsNullOrWhiteSpace(country)) q = q.Where(l => l.Country == country.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(l => EF.Functions.ILike(l.CompanyName, term) || (l.Email != null && EF.Functions.ILike(l.Email, term)) || (l.City != null && EF.Functions.ILike(l.City, term)) || (l.Region != null && EF.Functions.ILike(l.Region, term)));
        }
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(l => l.Status == GlobalLeadStatus.Pending).ThenBy(l => l.NextSendAt == null).ThenBy(l => l.NextSendAt).ThenByDescending(l => l.Confidence).ThenByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<Dictionary<GlobalLeadStatus, int>> CountsByStatusAsync(CancellationToken cancellationToken = default) =>
        (await Leads.GroupBy(l => l.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.C);

    public async Task<Dictionary<OutreachStatus, int>> CountsByOutreachAsync(CancellationToken cancellationToken = default) =>
        (await Leads.Where(l => l.Status == GlobalLeadStatus.Approved).GroupBy(l => l.OutreachStatus).Select(g => new { g.Key, C = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.C);

    public async Task<Dictionary<string, int>> CountsByCountryAsync(CancellationToken cancellationToken = default) =>
        (await Leads.GroupBy(l => l.Country).Select(g => new { g.Key, C = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.C);

    public Task<GlobalLead?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        Leads.FirstOrDefaultAsync(l => l.Fingerprint == fingerprint, cancellationToken);

    public async Task<List<(Guid Id, string Name, string NormalizedName, string? NormalizedPhone, string? Domain, string? City)>> KnownAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Leads.Select(l => new { l.Id, l.CompanyName, l.NormalizedName, l.NormalizedPhone, l.Domain, l.City }).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.CompanyName, r.NormalizedName, r.NormalizedPhone, r.Domain, r.City)).ToList();
    }

    public async Task AddAsync(GlobalLead lead, CancellationToken cancellationToken = default)
    {
        await _context.GlobalLeads.AddAsync(lead, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(GlobalLead lead, CancellationToken cancellationToken = default)
    {
        lead.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(lead).State == EntityState.Detached) _context.GlobalLeads.Update(lead);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<GlobalLead>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var set = ids.ToList();
        return Leads.Where(l => set.Contains(l.Id)).ToListAsync(cancellationToken);
    }

    public Task<List<GlobalLead>> GetDueAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
        Leads.Where(l => l.Status == GlobalLeadStatus.Approved && l.OutreachStatus == OutreachStatus.Active && l.NextSendAt != null && l.NextSendAt <= nowUtc)
            .OrderBy(l => l.NextSendAt).Take(limit).ToListAsync(cancellationToken);

    public Task<List<GlobalLeadEmail>> GetEmailsAsync(Guid leadId, CancellationToken cancellationToken = default) =>
        Emails.Where(e => e.LeadId == leadId).OrderBy(e => e.SentAt).ToListAsync(cancellationToken);

    public async Task AddEmailAsync(GlobalLeadEmail email, CancellationToken cancellationToken = default)
    {
        await _context.GlobalLeadEmails.AddAsync(email, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountSentSinceAsync(Guid senderUserId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Emails.CountAsync(e => e.SenderUserId == senderUserId && e.Status == OutreachEmailStatus.Sent && e.SentAt >= sinceUtc, cancellationToken);

    public async Task<HashSet<string>> RecentlyScannedRegionsAsync(string country, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var prefix = $"{country.ToUpperInvariant()}:";
        var rows = await _context.LeadAgentRuns.IgnoreQueryFilters().Where(r => !r.IsDeleted && r.StartedAt >= sinceUtc && r.Cities.StartsWith(prefix)).Select(r => r.Cities).ToListAsync(cancellationToken);
        return rows.SelectMany(c => c[prefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
