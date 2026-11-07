namespace IonCrm.Application.Common.Models.ExternalApis;

// Liftdesk haftalık sürüm notları — EMS'teki gömülü release-notes.json'un salt okunur yükü
// (GET /api/v1/crm/release-notes; sözleşme "Sürüm Notları API (CRM)"). Firma bağımsız, kişisel veri yok;
// metinler görüntülemeye hazırdır, CRM çeviri/işleme yapmaz. Tarihler yyyy-MM-dd DÜZ METİN (saat dilimi yok).
// Bölüm anahtarı ve madde türü string tutulur: EMS yeni değer eklediğinde CRM kırılmaz (bilinmeyen → nötr ikon/rozet).

/// <summary>Yükün kökü. <paramref name="Releases"/> en yeni sürüm en başta.</summary>
public record LiftdeskReleaseNotes(string? Language, string? GeneratedAt, List<LiftdeskRelease>? Releases);

/// <summary>Bir haftalık sürüm. <paramref name="Id"/> kararlı — "görüldü" anahtarı.</summary>
public record LiftdeskRelease(
    string Id,
    string? Title,
    string? PeriodStart,
    string? PeriodEnd,
    string? Summary,
    List<LiftdeskReleaseSection>? Sections);

/// <summary>Konu bölümü; <paramref name="Key"/> ikon/sıra eşlemesi için (faults, documents, notifications, …).</summary>
public record LiftdeskReleaseSection(string? Key, string? Title, List<LiftdeskReleaseItem>? Items);

/// <summary>Tek madde; <paramref name="Type"/> new | improvement | fix (rozet), <paramref name="Note"/> isteğe bağlı ikinci satır.</summary>
public record LiftdeskReleaseItem(string? Type, string? Title, string? Note);
