namespace IonCrm.Domain.Enums;

/// <summary>Bağlı sosyal medya platformu. Instagram ayrı hesaptır ama token'ı bağlı Facebook Page'inkidir.</summary>
public enum SocialProvider
{
    Facebook = 1,
    Instagram = 2,
    X = 3,
}

/// <summary>
/// Bağlı hesabın kullanılabilirlik durumu. <see cref="Expiring"/> yalnız yenilenemeyen token'da
/// (Meta uzun ömürlü kullanıcı token'ı) anlamlıdır; X erişim token'ı refresh ile kendiliğinden yenilenir.
/// </summary>
public enum SocialAccountStatus
{
    Active = 1,
    Expiring = 2,
    Expired = 3,
    /// <summary>Platform token'ı geçersiz saydı (iptal, parola değişimi, uygulama kaldırıldı) — yeniden bağlanmalı.</summary>
    Revoked = 4,
    /// <summary>İzin eksikliği ya da tanımsız kalıcı hata — insan müdahalesi.</summary>
    Error = 5,
}

/// <summary>
/// Gönderi × hesap yayın işi. Platform başına tek satır olduğu için kısmi başarısızlıkta yalnız
/// başarısız platform yeniden denenir; başarılı iş bir daha ASLA koşmaz (mükerrer gönderi olmaz).
/// </summary>
public enum SocialPublishJobStatus
{
    Pending = 1,
    InProgress = 2,
    Succeeded = 3,
    /// <summary>Geçici hata — backoff ile yeniden denenecek.</summary>
    Failed = 4,
    /// <summary>Kalıcı hata ya da deneme tavanı — insan müdahalesi (yeniden dene / atla).</summary>
    Blocked = 5,
    /// <summary>İnsan atladı: bu platformda yayınlanmadı ama damgayı engellemesin.</summary>
    Skipped = 6,
}
