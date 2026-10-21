using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk corporate-site blog queue (docs/crm-site-blog-api.md in the Liftdesk repo).
/// Same static Bearer key as the social queue (<c>Liftdesk:ApiKey</c>). The CRM moderates (edit / approve /
/// reject / retire / delete), manages the topic queue and may pull the publish trigger early; the agent's
/// draft/context endpoints and the one-off import are not exposed here.
/// </summary>
public interface ILiftdeskSiteBlogClient
{
    bool IsConfigured { get; }

    /// <summary>GET locales — registry; order = screen order.</summary>
    Task<LiftdeskEnvelope<List<LiftdeskSiteLocale>>> GetLocalesAsync(CancellationToken cancellationToken = default);

    /// <summary>GET ?status=&amp;locale=&amp;page=&amp;pageSize= — topics with at least one variant in <paramref name="status"/>; variants without body.</summary>
    Task<LiftdeskEnvelope<LiftdeskSiteBlogPage>> ListAsync(string? status, string? locale, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>GET {postId} — all variants with Markdown.</summary>
    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> GetAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>PUT variants/{id} — validator runs server-side (400 SITE_BLOG_VALIDATION + errors[]).</summary>
    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> UpdateVariantAsync(Guid variantId, LiftdeskSiteBlogVariantUpdate body, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> ApproveVariantAsync(Guid variantId, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> RejectVariantAsync(Guid variantId, string? reason, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> RetireVariantAsync(Guid variantId, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSiteBlogPost>> ApproveAllAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>DELETE {postId} — soft delete; 409 when a variant is Published/Retired.</summary>
    Task<LiftdeskEnvelope<object>> DeleteAsync(Guid postId, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSitePublishResult>> PublishAsync(CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSitePublishStatus>> GetPublishStatusAsync(CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<List<LiftdeskSiteBlogTopicRequest>>> GetTopicRequestsAsync(string? status, CancellationToken cancellationToken = default);

    Task<LiftdeskEnvelope<LiftdeskSiteBlogTopicRequest>> CreateTopicRequestAsync(string topic, string? group, string? note, CancellationToken cancellationToken = default);

    /// <summary>DELETE topic-requests/{id} — Open → Dropped (409 otherwise).</summary>
    Task<LiftdeskEnvelope<LiftdeskSiteBlogTopicRequest>> DropTopicRequestAsync(Guid id, CancellationToken cancellationToken = default);
}
