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

    /// <summary>"tr" for Turkey / unknown country, otherwise "en".</summary>
    public static string LanguageFor(string? country) =>
        string.IsNullOrWhiteSpace(country) || country.Equals("TR", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
}
