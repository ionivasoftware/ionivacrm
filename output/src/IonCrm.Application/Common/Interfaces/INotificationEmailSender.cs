namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Operatör bildirim e-postalarını gönderir (yedekleme uyarısı, ödeme bildirimi, CRM işlemleri).
///
/// TASARIM: Bildirim gönderimi HİÇBİR ZAMAN iş akışını bozmaz. Uygulama bir aboneliği uzattıysa,
/// SMTP çöktü diye o işlem başarısız sayılamaz — bu yüzden <see cref="SendAsync"/> istisna fırlatmaz,
/// hatayı loglayıp sessizce döner. Yapılandırma yoksa (SMTP host/alıcı tanımsız) hiçbir şey yapmaz.
/// </summary>
public interface INotificationEmailSender
{
    /// <summary>SMTP host + en az bir alıcı tanımlıysa true; değilse gönderim atlanır.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Bildirimi gönderir. Hata fırlatmaz — başarısızlık loglanır ve <c>false</c> döner.
    /// </summary>
    /// <param name="subject">Konu (önüne ortam etiketi eklenir).</param>
    /// <param name="htmlBody">Gövde (HTML).</param>
    Task<bool> SendAsync(string subject, string htmlBody, CancellationToken cancellationToken = default);
}
