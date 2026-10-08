using System.Text.Json;

namespace IonCrm.Application.Common.Models.ExternalApis;

// Kurumsal site (liftdesk.app) kullanım verisi — docs/crm-site-analytics-api.md.
// Ölçüm birinci taraf ve çerezsiz; IP saklanmaz. "visitors" GÜNLÜK tekil toplamıdır (§1): aynı
// kişi iki ayrı günde iki sayılır — ziyaretçi kimliği her gün değişen bir HMAC özeti olduğundan
// günler arası tekilleştirme mümkün değildir. Ekranda bu uyarı gösterilmelidir.

public record LiftdeskSiteAnalyticsTotals(
    int PageViews,
    int Visitors,
    int SignupStarts,
    int SignupVerified,
    /// <summary>Scroll olaylarının ortalaması (yüzde); olay yoksa null.</summary>
    double? AverageScrollDepth);

/// <summary>Yalnız olay olan günler gelir — boş gün satırı YOK; CRM eksik günleri 0 ile doldurur.</summary>
public record LiftdeskSiteAnalyticsDaily(string Day, int PageViews, int Visitors, int SignupStarts, int SignupVerified);

public record LiftdeskSiteAnalyticsCountry(string Country, int PageViews, int Visitors, int SignupStarts, int SignupVerified);

public record LiftdeskSiteAnalyticsPage(string Path, string? Site, int PageViews, int Visitors);

/// <summary>Genel kırılım satırı (site/referrer/device/language/campaign/section/click/scrollDepth).</summary>
public record LiftdeskSiteAnalyticsBucket(string Key, int Count, int Visitors);

/// <summary>Site başına kayıt hunisi: kayıt sayfası görüntüleme → kod istendi → firma açıldı.</summary>
public record LiftdeskSiteAnalyticsFunnel(string Site, int SignupPageViews, int SignupStarts, int SignupVerified);

public record LiftdeskSiteAnalyticsOverview(
    string From,
    string To,
    string? Site,
    string? Country,
    LiftdeskSiteAnalyticsTotals Totals,
    List<LiftdeskSiteAnalyticsDaily>? Daily,
    List<LiftdeskSiteAnalyticsCountry>? Countries,
    List<LiftdeskSiteAnalyticsPage>? Pages,
    List<LiftdeskSiteAnalyticsBucket>? Sites,
    List<LiftdeskSiteAnalyticsBucket>? Referrers,
    List<LiftdeskSiteAnalyticsBucket>? Devices,
    List<LiftdeskSiteAnalyticsBucket>? Languages,
    List<LiftdeskSiteAnalyticsBucket>? Campaigns,
    List<LiftdeskSiteAnalyticsBucket>? Sections,
    List<LiftdeskSiteAnalyticsBucket>? Clicks,
    List<LiftdeskSiteAnalyticsBucket>? ScrollDepth,
    List<LiftdeskSiteAnalyticsFunnel>? Funnel);

/// <summary>Ham olay (§5). Ziyaretçi özeti bilerek yok — CRM kişi takip etmez.</summary>
public record LiftdeskSiteAnalyticsEvent(
    DateTime OccurredAt,
    string Type,
    string Path,
    string? Site,
    string? Country,
    string? Referrer,
    string? Device,
    string? Language,
    string? Label,
    /// <summary>Scroll'da 25/50/75/100 (sayı), diğerlerinde null — ham JSON olarak taşınır.</summary>
    JsonElement? Value,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign);
