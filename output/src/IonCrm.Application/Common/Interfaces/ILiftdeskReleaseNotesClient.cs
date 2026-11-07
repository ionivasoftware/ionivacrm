using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Read-only M2M client for Liftdesk's weekly release notes (same static Bearer key as the other CRM read
/// endpoints, <c>Liftdesk:ApiKey</c>). The payload is tenant-independent and changes only when EMS ships a
/// release, so implementations cache a successful answer briefly — the sidebar badge asks for it on every login.
/// </summary>
public interface ILiftdeskReleaseNotesClient
{
    bool IsConfigured { get; }

    /// <summary>GET /api/v1/crm/release-notes — newest release first.</summary>
    Task<LiftdeskEnvelope<LiftdeskReleaseNotes>> GetAsync(CancellationToken cancellationToken = default);
}
