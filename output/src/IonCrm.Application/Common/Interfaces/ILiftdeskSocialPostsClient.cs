using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk social-media post queue (docs/crm-social-media-api.md in the Liftdesk
/// repo). Auth is the static Bearer key <c>Liftdesk:ApiKey</c> (CRM__APIKEY — shared with backups,
/// tickets, site analytics). The CRM moderates (edit / approve / reject / delete); the agent writes
/// drafts and the publisher posts — neither of those goes through this client.
/// </summary>
public interface ILiftdeskSocialPostsClient
{
    bool IsConfigured { get; }

    /// <summary>GET /api/v1/crm/social-posts — status + scheduled-day range filter, paged.</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPostPage>> ListAsync(
        string? status, string? from, string? to, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>GET /api/v1/crm/social-posts/{id} — detail with all language variants.</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPost>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>GET /api/v1/crm/social-posts/{id}/card?lang= — template card PNG (1080×1080).</summary>
    Task<LiftdeskEnvelope<LiftdeskBinary>> GetCardAsync(Guid id, string lang, CancellationToken cancellationToken = default);

    /// <summary>PUT /api/v1/crm/social-posts/{id} — only Pending/Rejected posts (409 otherwise).</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPost>> UpdateAsync(
        Guid id, LiftdeskSocialPostUpdate body, CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/crm/social-posts/{id}/approve — Pending/Rejected → Approved.</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPost>> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/crm/social-posts/{id}/reject — Pending/Approved → Rejected.</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPost>> RejectAsync(
        Guid id, string? reason, CancellationToken cancellationToken = default);

    /// <summary>DELETE /api/v1/crm/social-posts/{id} — soft delete, frees the slot. Published → 404.</summary>
    Task<LiftdeskEnvelope<object>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/crm/social-posts/{id}/published — Approved → Published (idempotent: an existing stamp is kept).</summary>
    Task<LiftdeskEnvelope<LiftdeskSocialPost>> MarkPublishedAsync(Guid id, DateTime? publishedAt, CancellationToken cancellationToken = default);
}
