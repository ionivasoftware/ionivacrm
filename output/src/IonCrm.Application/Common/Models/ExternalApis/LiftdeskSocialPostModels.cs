namespace IonCrm.Application.Common.Models.ExternalApis;

// Liftdesk sosyal medya gönderi kuyruğu — docs/crm-social-media-api.md (Liftdesk deposu).
// Liftdesk'in KENDİ hesapları (FB/IG/X) için ajan taslak üretir, onay CRM'de verilir, yayınlama
// AYRI iştir (publisher). Kind/Status Liftdesk'te JsonStringEnumConverter ile İSİM olarak gelir;
// burada bilerek string tutulur — Liftdesk yeni bir değer eklediğinde CRM çözümleme hatasıyla
// kırılmasın (bkz. wire-DTO tuzağı notu: alan eksikse sessizce düşer, enum bilinmiyorsa patlar).

/// <summary>Bir dilin yayınlanabilir metni. Language: tr | en | en-GB (en-GB "lift", en "elevator").</summary>
public record LiftdeskSocialPostVariant(
    string Language,
    /// <summary>Şablon kartın başlığı (≤ 80).</summary>
    string CardTitle,
    /// <summary>Facebook/Instagram metni — hashtag'siz (≤ 4000).</summary>
    string Body,
    /// <summary>X metni — hashtag DAHİL ≤ 280.</summary>
    string BodyShort,
    /// <summary>Boşlukla ayrılmış etiketler (≤ 300).</summary>
    string Hashtags);

/// <summary>Liste/detay satırı. Variants tr → en → en-GB sıralı gelir.</summary>
public record LiftdeskSocialPost(
    Guid Id,
    /// <summary>News | Info | Product | Holiday | Commemoration.</summary>
    string Kind,
    /// <summary>Pending | Approved | Rejected | Published.</summary>
    string Status,
    string SlotKey,
    /// <summary>Yayın GÜNÜ (saf gün, 00:00Z). Saat politikası publisher'da.</summary>
    DateTime ScheduledFor,
    /// <summary>Türkçe konu özeti.</summary>
    string Topic,
    string? HolidayName,
    /// <summary>Satır başına bir URL (yalnız News'te dolu).</summary>
    string? SourceUrls,
    /// <summary>Doluysa publisher bunu kullanır; boşsa şablon kart (GET {id}/card).</summary>
    string? CustomImageUrl,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string? RejectReason,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<LiftdeskSocialPostVariant>? Variants);

/// <summary>GET /crm/social-posts sayfası.</summary>
public record LiftdeskSocialPostPage(List<LiftdeskSocialPost>? Items, int TotalCount, int Page, int PageSize);

/// <summary>
/// PUT {id} gövdesi. Null alan = dokunma. CustomImageUrl "" = şablon karta dön. Variants dil
/// bazında kısmî güncellenir (gönderilmeyen dil olduğu gibi kalır; olmayan dil eklenir).
/// </summary>
public record LiftdeskSocialPostUpdate(
    string? Topic,
    string? CustomImageUrl,
    List<LiftdeskSocialPostVariant>? Variants);

/// <summary>İkili yanıt (şablon kart PNG'si).</summary>
public record LiftdeskBinary(byte[] Content, string ContentType);
