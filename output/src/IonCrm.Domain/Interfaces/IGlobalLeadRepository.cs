using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Global (non-TR) leads and their outreach e-mail log. Global tables, SuperAdmin-only screen.</summary>
public interface IGlobalLeadRepository
{
    Task<GlobalLead?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GlobalLead?> GetByUnsubscribeTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<(List<GlobalLead> Items, int TotalCount)> ListAsync(GlobalLeadStatus? status, OutreachStatus? outreach, string? country, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Dictionary<GlobalLeadStatus, int>> CountsByStatusAsync(CancellationToken cancellationToken = default);

    Task<Dictionary<OutreachStatus, int>> CountsByOutreachAsync(CancellationToken cancellationToken = default);

    Task<Dictionary<string, int>> CountsByCountryAsync(CancellationToken cancellationToken = default);

    Task<GlobalLead?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    Task<List<(Guid Id, string Name, string NormalizedName, string? NormalizedPhone, string? Domain, string? City)>> KnownAsync(CancellationToken cancellationToken = default);

    Task AddAsync(GlobalLead lead, CancellationToken cancellationToken = default);

    Task UpdateAsync(GlobalLead lead, CancellationToken cancellationToken = default);

    Task<List<GlobalLead>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Active leads whose NextSendAt has passed, oldest first.</summary>
    Task<List<GlobalLead>> GetDueAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default);

    Task<List<GlobalLeadEmail>> GetEmailsAsync(Guid leadId, CancellationToken cancellationToken = default);

    Task AddEmailAsync(GlobalLeadEmail email, CancellationToken cancellationToken = default);

    /// <summary>Successfully sent e-mails by this sender since <paramref name="sinceUtc"/> (daily cap).</summary>
    Task<int> CountSentSinceAsync(Guid senderUserId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Regions scanned (per country) by agent runs started after <paramref name="sinceUtc"/>.</summary>
    Task<HashSet<string>> RecentlyScannedRegionsAsync(string country, DateTime sinceUtc, CancellationToken cancellationToken = default);
}
