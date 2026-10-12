using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.SocialPublishing;

/// <summary>
/// Default profile copy for Liftdesk's own accounts — what the screen shows until an operator edits it.
/// Field keys are the contract between this class, the controller's validation and the frontend schema.
/// Pushable Facebook keys: about, description, website, phone, email. Everything else is reference copy
/// the operator applies by hand (page name/category/CTA are not writable through the Pages API; the
/// Instagram API has no profile-write at all).
/// </summary>
public static class SocialProfileDefaults
{
    public const string DefaultLanguage = "tr";

    /// <summary>Facebook "about" hard limit (Graph rejects longer).</summary>
    public const int FacebookAboutMax = 255;

    /// <summary>Instagram bio hard limit.</summary>
    public const int InstagramBioMax = 150;

    /// <summary>Instagram display name hard limit.</summary>
    public const int InstagramNameMax = 64;

    public static readonly string[] FacebookKeys =
        { "name", "username", "category", "about", "description", "website", "phone", "email", "address", "cta", "cover_text" };

    public static readonly string[] InstagramKeys =
        { "name", "username", "category", "bio", "website", "contact_email", "cta", "highlights", "hashtags" };

    /// <summary>Keys the Facebook Pages API accepts on POST /{page-id} (mapped by the client).</summary>
    public static readonly string[] FacebookPushableKeys = { "about", "description", "website", "phone", "email" };

    public static IReadOnlyList<string> KeysFor(SocialProvider provider) => provider switch
    {
        SocialProvider.Facebook => FacebookKeys,
        SocialProvider.Instagram => InstagramKeys,
        _ => Array.Empty<string>(),
    };

    public static Dictionary<string, string> For(SocialProvider provider, string language)
    {
        // Only Turkish copy exists today; other languages start from the same keys, empty.
        if (!string.Equals(language, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            return KeysFor(provider).ToDictionary(k => k, _ => string.Empty);

        return provider switch
        {
            SocialProvider.Facebook => new Dictionary<string, string>
            {
                ["name"] = "LiftDesk",
                ["username"] = "@liftdesk",
                ["category"] = "Yazılım Şirketi · İnternet Şirketi",
                ["about"] = "Asansör firmaları için bakım, arıza, teklif ve saha yönetimi tek panelde. QR kodlu dijital föy, mobil saha uygulaması, müşteri portalı. liftdesk.app",
                ["description"] =
                    "LiftDesk, asansör bakım firmalarının tüm operasyonunu tek akışta yöneten bulut yazılımıdır: bakımdan tahsilata.\n\n" +
                    "• Periyodik bakım planlama — sözleşmedeki bakım günleri takvime otomatik düşer, atlanan bakım anında görünür\n" +
                    "• QR kodlu dijital bakım föyü — teknisyen asansördeki QR'ı okutur, föy sahada dolar; kâğıt ve sonradan toplu giriş biter\n" +
                    "• Arıza yönetimi — kayıt, teknisyen ataması ve müşteri bilgilendirmesi tek akışta\n" +
                    "• Mobil saha uygulaması — çevrimdışı föy, imza ve fotoğraf sahada alınır\n" +
                    "• Müşteri portalı ve müşteri mobil uygulaması — bina yöneticisi bakım geçmişini, arıza durumunu ve teklifleri kendi ekranından izler\n" +
                    "• Teklif ve revizyon sihirbazı, bakım sözleşmesi ve cari/tahsilat takibi, Paraşüt entegrasyonu\n" +
                    "• Yönetici raporları — bakım, arıza, tahsilat ve sözleşme verisinden ay sonu özetleri\n\n" +
                    "Türkçe ve İngilizce arayüz; Birleşik Krallık için LOLER uyumlu denetim kayıtları.\n\n" +
                    "Ücretsiz deneyin: https://liftdesk.app",
                ["website"] = "https://liftdesk.app",
                ["phone"] = "",
                ["email"] = "",
                ["address"] = "",
                ["cta"] = "Sayfa düğmesi: \"Daha Fazla Bilgi Al\" → https://liftdesk.app",
                ["cover_text"] = "Kapak görseli metni önerisi: \"Asansör bakım programı: bakımdan tahsilata kadar tek akış\" — alt satır: QR föy · Mobil saha · Müşteri portalı · liftdesk.app",
            },
            SocialProvider.Instagram => new Dictionary<string, string>
            {
                ["name"] = "LiftDesk · Asansör Bakım Yazılımı",
                ["username"] = "liftdesk",
                ["category"] = "Yazılım Şirketi",
                ["bio"] = "Asansör firmaları için bakım, arıza, teklif ve saha yönetimi tek panelde 🛗\n📱 QR föy · mobil saha · müşteri portalı\n👇 Ücretsiz dene",
                ["website"] = "https://liftdesk.app",
                ["contact_email"] = "",
                ["cta"] = "Eylem düğmeleri: E-posta gönder · Web sitesi. Profesyonel hesap türü: İşletme (Business).",
                ["highlights"] = "Öne çıkanlar önerisi: Özellikler · Sahada · Müşteri Portalı · SSS · Ekip",
                ["hashtags"] = "#asansör #asansörbakım #asansörfirması #bakımyönetimi #sahayönetimi #liftdesk",
            },
            _ => new Dictionary<string, string>(),
        };
    }
}
