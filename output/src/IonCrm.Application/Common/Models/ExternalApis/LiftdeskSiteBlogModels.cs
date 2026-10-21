namespace IonCrm.Application.Common.Models.ExternalApis;

// Liftdesk kurumsal site blog kuyruğu — docs/crm-site-blog-api.md (Liftdesk deposu). Ajan (EMS tarafı)
// haftada bir konu yazar (Pending), onay CRM'de verilir, yayın tetiğini EMS çeker (site yeniden derlenir).
// Enum'lar Liftdesk'te ad olarak yazılır; burada string tutulur (yeni değer gelince CRM kırılmasın).
// Yerel ayar listesi SABİT DEĞİL — GET locales'ten gelir; ekran sekmelerini oradan kurar.

/// <summary>Site yerel ayarı (GET locales). Sıra = ekran sırası.</summary>
public record LiftdeskSiteLocale(
    string Key,
    string DisplayName,
    string Language,
    string Spelling,
    string? Country,
    string Lang,
    string Home,
    string BlogPath,
    string FaqHeading,
    string HowItWorksHeading,
    bool WriterEnabled);

/// <summary>Bir yerel ayardaki yazı. Listede BodyMarkdown null, detayda dolu. Status: Pending | Approved | Published | Rejected | Retired.</summary>
public record LiftdeskSiteBlogVariant(
    Guid Id,
    string Locale,
    string Slug,
    string Url,
    string Title,
    string Description,
    string? BodyMarkdown,
    string? RelatedPageSlug,
    /// <summary>yyyy-MM-dd (sitede görünen tarih).</summary>
    string PublishDate,
    string? UpdatedDate,
    int Words,
    string Status,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string? RejectReason,
    DateTime? PublishedAt,
    DateTime? LiveAt,
    DateTime UpdatedAt);

/// <summary>Konu + varyantları. Source: Queue | Rotation | Manual. OverallStatus sunucu türetir.</summary>
public record LiftdeskSiteBlogPost(
    Guid Id,
    string Group,
    string Topic,
    string Source,
    Guid? TopicRequestId,
    List<string>? SourceUrls,
    DateTime CreatedAt,
    string OverallStatus,
    List<LiftdeskSiteBlogVariant>? Variants);

public record LiftdeskSiteBlogPage(List<LiftdeskSiteBlogPost>? Items, int TotalCount, int Page, int PageSize);

/// <summary>PUT variants/{id} — gönderilmeyen alan olduğu gibi kalır; RelatedPageSlug "" temizler.</summary>
public record LiftdeskSiteBlogVariantUpdate(
    string? Title,
    string? Description,
    string? Slug,
    string? BodyMarkdown,
    string? RelatedPageSlug,
    string? PublishDate);

/// <summary>Konu kuyruğu isteği. Status: Open | Taken | Done | Dropped.</summary>
public record LiftdeskSiteBlogTopicRequest(
    Guid Id,
    string Topic,
    string? Group,
    string? Note,
    string Status,
    Guid? TakenByPostId,
    DateTime CreatedAt);

/// <summary>POST publish yanıtı. Reason: not-configured | nothing-to-publish | debounced | trigger-failed.</summary>
public record LiftdeskSitePublishResult(bool Triggered, string? Reason, int AwaitingPublish, DateTime? TriggeredAt);

public record LiftdeskSiteAwaitingLive(Guid VariantId, string Locale, string Slug, string Url, DateTime PublishedAt, double HoursWaiting);

public record LiftdeskSitePublishStatus(
    bool TriggerConfigured,
    DateTime? LastTriggeredAt,
    string? LastTriggerOutcome,
    int AwaitingPublish,
    List<LiftdeskSiteAwaitingLive>? AwaitingLive,
    List<string>? Warnings);
