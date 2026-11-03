using System.Security.Cryptography;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.SupportAccess;

/// <summary>Pure rules for support-access consent (unit-tested): validity window, link lifetime, what a login needs.</summary>
public static class SupportAccessRules
{
    public const int DefaultValidHours = 2;
    public const int MinValidHours = 1;
    public const int MaxValidHours = 24;
    /// <summary>An unanswered request link stops working after this many days.</summary>
    public const int LinkValidDays = 7;
    public const int ReasonMin = 10;
    public const int ReasonMax = 500;

    public static int ClampHours(int? hours) => Math.Clamp(hours ?? DefaultValidHours, MinValidHours, MaxValidHours);

    /// <summary>32-char URL-safe random token.</summary>
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool IsActive(SupportAccessRequest r, DateTime nowUtc) =>
        r.Status == SupportAccessStatus.Approved && r.ExpiresAt is { } e && e > nowUtc;

    public static bool IsPending(SupportAccessRequest r, DateTime nowUtc) =>
        r.Status == SupportAccessStatus.Pending && r.LinkExpiresAt > nowUtc;

    /// <summary>Null when the customer may still answer through the link, else the (Turkish) reason the link is dead.</summary>
    public static string? WhyCannotRespond(SupportAccessRequest r, DateTime nowUtc) => r.Status switch
    {
        SupportAccessStatus.Approved => "Bu isteğe zaten izin verilmiş.",
        SupportAccessStatus.Declined => "Bu istek zaten reddedilmiş.",
        SupportAccessStatus.Cancelled => "Bu isteğin yerini yeni bir istek aldı.",
        _ => r.LinkExpiresAt > nowUtc ? null : "Bu isteğin süresi dolmuş.",
    };

    /// <summary>Null when a support login may be opened under this request now, else the (Turkish) reason.</summary>
    public static string? WhyCannotLogin(SupportAccessRequest r, Guid customerId, string emsUserId, DateTime nowUtc)
    {
        if (r.CustomerId != customerId || !string.Equals(r.EmsUserId, emsUserId, StringComparison.Ordinal))
            return "İzin isteği bu müşteri/kullanıcıya ait değil.";
        return r.Status switch
        {
            SupportAccessStatus.Pending => r.LinkExpiresAt > nowUtc ? "Müşteri izin isteğini henüz yanıtlamadı." : "İzin isteği yanıtlanmadan süresi doldu; yeniden isteyin.",
            SupportAccessStatus.Declined => "Müşteri destek erişimine izin vermedi.",
            SupportAccessStatus.Cancelled => "Bu isteğin yerini yeni bir istek aldı.",
            SupportAccessStatus.Approved => r.ExpiresAt is { } e && e > nowUtc ? null : $"İzin süresi doldu ({r.ExpiresAt:dd.MM.yyyy HH:mm} UTC); yeniden isteyin.",
            _ => "İzin durumu bilinmiyor.",
        };
    }

    /// <summary>Applies the customer's answer; returns false when the link can no longer be used.</summary>
    public static bool Respond(SupportAccessRequest r, bool approve, DateTime nowUtc)
    {
        if (WhyCannotRespond(r, nowUtc) is not null) return false;
        r.RespondedAt = nowUtc;
        r.Status = approve ? SupportAccessStatus.Approved : SupportAccessStatus.Declined;
        r.ExpiresAt = approve ? nowUtc.AddHours(ClampHours(r.ValidHours)) : null;
        r.UpdatedAt = nowUtc;
        return true;
    }

    // ── Mail language ────────────────────────────────────────────────────────

    public const string DefaultLanguage = "tr";

    /// <summary>Languages the consent mail and the public pages exist in.</summary>
    public static readonly IReadOnlyList<string> MailLanguages = new[] { "tr", "en" };

    /// <summary>Where the chosen language came from (shown in the dialog, written to the timeline).</summary>
    public const string SourceOperator = "seçim";
    public const string SourceFirm = "firma dili";
    public const string SourceCountry = "ülke";
    public const string SourcePhone = "telefon";
    public const string SourceDefault = "varsayılan";

    public sealed record LanguageChoice(string Language, string Source);

    /// <summary>"tr" / "en" for a known mail language ("EN-gb", " tr " accepted), otherwise null.</summary>
    public static string? NormalizeMailLanguage(string? value)
    {
        var primary = PrimarySubtag(value);
        return primary is not null && MailLanguages.Contains(primary) ? primary : null;
    }

    /// <summary>
    /// The language the customer is written to: the operator's explicit pick, else the firm's own Liftdesk language
    /// (feed <c>language</c>), else its signup country, else an international phone prefix, else Turkish. Only Turkish
    /// firms get the Turkish mail; every other language/country gets English (no further translations yet).
    /// </summary>
    public static LanguageChoice ResolveLanguage(string? firmLanguage, string? country, string? phone, string? requested = null)
    {
        if (NormalizeMailLanguage(requested) is { } picked) return new(picked, SourceOperator);
        if (PrimarySubtag(firmLanguage) is { } firm) return new(firm == "tr" ? "tr" : "en", SourceFirm);
        var c = country?.Trim().ToUpperInvariant();
        if (c is { Length: 2 } && c.All(char.IsLetter)) return new(c == "TR" ? "tr" : "en", SourceCountry);
        if (CallingCode(phone) is { } cc) return new(cc == "90" ? "tr" : "en", SourcePhone);
        return new(DefaultLanguage, SourceDefault);
    }

    /// <summary>"en-GB" / "TR" / " tr_TR " → "en" / "tr"; anything that is not a 2–3 letter language code → null.</summary>
    private static string? PrimarySubtag(string? value)
    {
        var primary = value?.Trim().ToLowerInvariant().Split('-', '_')[0];
        return primary is { Length: 2 or 3 } && primary.All(ch => ch is >= 'a' and <= 'z') ? primary : null;
    }

    /// <summary>
    /// Country calling code when the number is written internationally ("+356 2122 3344" → "356", "0090 555…" → "90");
    /// null for national formats ("0555 …") which say nothing about the country. Only 1–3 digit codes are told apart
    /// well enough for the tr/en decision: anything not starting with 90 counts as abroad.
    /// </summary>
    private static string? CallingCode(string? phone)
    {
        var p = phone?.Trim();
        if (string.IsNullOrEmpty(p)) return null;
        var digits = new string(p.Where(char.IsDigit).ToArray());
        var intl = p.StartsWith('+') ? digits : p.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : null;
        if (intl is null || intl.Length < 8) return null;
        return intl.StartsWith("90", StringComparison.Ordinal) ? "90" : intl[..Math.Min(3, intl.Length)];
    }
}
