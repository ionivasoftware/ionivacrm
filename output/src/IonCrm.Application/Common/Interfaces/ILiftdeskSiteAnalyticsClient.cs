using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Read-only M2M client for the Liftdesk corporate-site analytics API (docs/crm-site-analytics-api.md).
/// Auth is the static Bearer key <c>Liftdesk:ApiKey</c> (CRM__APIKEY — shared with backups, tickets,
/// error-triage). Data is first-party, cookieless and contains no personal data; the CRM only reads.
/// </summary>
public interface ILiftdeskSiteAnalyticsClient
{
    bool IsConfigured { get; }

    /// <summary>GET /api/v1/crm/site-analytics/overview — dönem toplamları, günlük seri, kırılımlar, huni.</summary>
    Task<LiftdeskEnvelope<LiftdeskSiteAnalyticsOverview>> GetOverviewAsync(
        string? from, string? to, string? site, string? country, CancellationToken cancellationToken = default);

    /// <summary>GET /api/v1/crm/site-analytics/events — son ham olaylar, yeniden eskiye.</summary>
    Task<LiftdeskEnvelope<List<LiftdeskSiteAnalyticsEvent>>> GetEventsAsync(
        string? from, string? to, string? site, string? country, string? type, int limit,
        CancellationToken cancellationToken = default);
}
