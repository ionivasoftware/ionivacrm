using System.Security.Cryptography;
using System.Text;
using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.Lifecycle;

/// <summary>Scheduler knobs (config <c>LifecycleMail:*</c>).</summary>
public record LifecycleOptions(
    int CheckInAfterDays = 3,
    int TrialEndedAfterDays = 1,
    /// <summary>Customers/expiries older than this are never mailed retroactively (feature switch-on safety).</summary>
    int MaxAgeDays = 14,
    int SendHourStart = 8,
    int SendHourEnd = 20,
    /// <summary>ISO2 codes never mailed (TR is handled by the call plan).</summary>
    string ExcludeCountries = "TR");

/// <summary>What the planner needs to know about a customer — a projection, not the entity.</summary>
public record LifecycleCustomer(
    Guid Id,
    CustomerStatus Status,
    DateTime CreatedAt,
    DateTime? ExpirationDate,
    string? Country,
    string? Email,
    bool IsInternal,
    bool MarketingOptOut,
    string? LegacyId);

/// <summary>
/// Pure rules for the demo lifecycle sequence (unit-tested): who is eligible, which step is due, in what
/// language and inside which local send window. Status comes from the Liftdesk sync (Demo = short trial
/// running, Passive = short trial expired without purchase, Active = bought).
/// </summary>
public static class DemoLifecyclePlanner
{
    private const int ShortTrialMaxDays = 40; // same boundary the sync uses for Demo/Passive vs Active/Churned

    private static readonly Dictionary<string, string> LanguageByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = "en", ["IE"] = "en", ["US"] = "en", ["CA"] = "en", ["AU"] = "en", ["NZ"] = "en", ["ZA"] = "en", ["IN"] = "en", ["SG"] = "en", ["AE"] = "en",
        ["DE"] = "de", ["AT"] = "de", ["CH"] = "de",
        ["FR"] = "fr", ["BE"] = "fr", ["NL"] = "nl", ["ES"] = "es", ["IT"] = "it", ["PT"] = "pt", ["TR"] = "tr",
    };

    private static readonly Dictionary<string, string> TimeZoneByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = "Europe/London", ["IE"] = "Europe/Dublin", ["PT"] = "Europe/Lisbon",
        ["DE"] = "Europe/Berlin", ["AT"] = "Europe/Vienna", ["CH"] = "Europe/Zurich", ["FR"] = "Europe/Paris", ["BE"] = "Europe/Brussels",
        ["NL"] = "Europe/Amsterdam", ["ES"] = "Europe/Madrid", ["IT"] = "Europe/Rome", ["TR"] = "Europe/Istanbul",
        ["US"] = "America/New_York", ["CA"] = "America/Toronto", ["AU"] = "Australia/Sydney", ["NZ"] = "Pacific/Auckland",
        ["ZA"] = "Africa/Johannesburg", ["IN"] = "Asia/Kolkata", ["SG"] = "Asia/Singapore", ["AE"] = "Asia/Dubai",
    };

    /// <summary>Template language for a country; "en" when unknown.</summary>
    public static string LanguageFor(string? country) =>
        country is { Length: 2 } && LanguageByCountry.TryGetValue(country, out var l) ? l : "en";

    /// <summary>A representative country for a template language (sample data / test sends): en→GB, de→DE, …</summary>
    public static string SampleCountryFor(string? language) => (language ?? "en").ToLowerInvariant() switch
    {
        "de" => "DE", "fr" => "FR", "nl" => "NL", "es" => "ES", "it" => "IT", "pt" => "PT", "tr" => "TR", _ => "GB",
    };

    /// <summary>Picks the language whose template folder exists: country language → "en".</summary>
    public static string ResolveLanguage(string? country, Func<string, bool> templateExists)
    {
        var lang = LanguageFor(country);
        if (templateExists(lang)) return lang;
        return "en";
    }

    public static TimeZoneInfo TimeZoneFor(string? country)
    {
        var id = country is { Length: 2 } && TimeZoneByCountry.TryGetValue(country, out var tz) ? tz : "Europe/London";
        foreach (var candidate in new[] { id, "Europe/London" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>True between SendHourStart and SendHourEnd in the customer's local time (any day of the week — onboarding mail should not wait a weekend).</summary>
    public static bool InSendWindow(DateTime nowUtc, string? country, LifecycleOptions o)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TimeZoneFor(country));
        return local.Hour >= o.SendHourStart && local.Hour < o.SendHourEnd;
    }

    public static bool IsExcludedCountry(string? country, LifecycleOptions o) =>
        string.IsNullOrWhiteSpace(country) ||
        o.ExcludeCountries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(c => c.Equals(country, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when the customer is in scope, otherwise the (Turkish) reason it is skipped.</summary>
    public static string? IneligibleReason(LifecycleCustomer c, LifecycleOptions o)
    {
        if (c.LegacyId is null || !c.LegacyId.StartsWith("LIFT-", StringComparison.Ordinal)) return "Liftdesk kaydı değil";
        if (c.IsInternal) return "iç hesap";
        if (string.IsNullOrWhiteSpace(c.Country)) return "ülke bilgisi yok";
        if (IsExcludedCountry(c.Country, o)) return $"{c.Country} kapsam dışı";
        if (string.IsNullOrWhiteSpace(c.Email) || !c.Email.Contains('@')) return "e-posta yok";
        if (c.MarketingOptOut) return "abonelikten çıkmış";
        return null;
    }

    private static bool IsShortTrial(LifecycleCustomer c) =>
        c.ExpirationDate is { } exp && (exp - c.CreatedAt).TotalDays <= ShortTrialMaxDays;

    /// <summary>
    /// The step that is due now, or null. <paramref name="sent"/> = steps already sent (any status). Rules:
    /// Welcome — Demo, created within MaxAgeDays. CheckIn — Demo, CheckInAfterDays after creation, trial still
    /// running. TrialEnded — Passive (short trial expired without purchase), TrialEndedAfterDays after expiry,
    /// expiry within MaxAgeDays. Steps are independent: a customer that predates the feature can still get
    /// the check-in or the reminder.
    /// </summary>
    public static LifecycleStep? NextStep(LifecycleCustomer c, IReadOnlySet<LifecycleStep> sent, DateTime nowUtc, LifecycleOptions o)
    {
        if (IneligibleReason(c, o) is not null) return null;
        var maxAge = TimeSpan.FromDays(o.MaxAgeDays);

        if (c.Status == CustomerStatus.Demo && IsShortTrial(c))
        {
            if (!sent.Contains(LifecycleStep.Welcome) && nowUtc - c.CreatedAt <= maxAge)
                return LifecycleStep.Welcome;
            if (!sent.Contains(LifecycleStep.CheckIn)
                && nowUtc >= c.CreatedAt.AddDays(o.CheckInAfterDays)
                && c.ExpirationDate > nowUtc
                && nowUtc - c.CreatedAt <= maxAge + TimeSpan.FromDays(o.CheckInAfterDays))
                return LifecycleStep.CheckIn;
            return null;
        }

        if (c.Status == CustomerStatus.Passive && IsShortTrial(c) && c.ExpirationDate is { } exp)
        {
            if (!sent.Contains(LifecycleStep.TrialEnded)
                && nowUtc >= exp.AddDays(o.TrialEndedAfterDays)
                && nowUtc - exp <= maxAge)
                return LifecycleStep.TrialEnded;
        }
        return null;
    }

    /// <summary>Opaque, stable unsubscribe token for a customer (HMAC of the id with the app secret) — URL-safe, 32 chars.</summary>
    public static string UnsubscribeToken(Guid customerId, string secret)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("lifecycle-unsub:" + customerId.ToString("N")));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..32];
    }

    public static bool VerifyUnsubscribeToken(Guid customerId, string secret, string token) =>
        token.Length == 32 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(UnsubscribeToken(customerId, secret)), Encoding.UTF8.GetBytes(token));
}

/// <summary>Template rendering for lifecycle mails: subjects from the leading HTML comments, simple placeholders.</summary>
public static class LifecycleTemplate
{
    public record Rendered(string Subject, string Html);

    public static string FileFor(LifecycleStep step) => step switch
    {
        LifecycleStep.Welcome => "welcome.html",
        LifecycleStep.CheckIn => "checkin.html",
        LifecycleStep.TrialEnded => "trial-ended.html",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    /// <summary>
    /// Placeholders: {{CompanyName}}, {{ContactName}} / [Name] (contact or company), {{TrialEndDate}} (local date),
    /// {{DaysLeft}}, {{PortalUrl}}, {{SupportEmail}}, {{UnsubscribeUrl}}. Subject = first <!-- 1: … --> comment.
    /// </summary>
    public static Rendered Render(string html, string companyName, string? contactName, DateTime? trialEndUtc, string? country, string portalUrl, string supportEmail, string unsubscribeUrl, DateTime nowUtc)
    {
        var subjects = Marketing.IntroMailTemplate.ExtractSubjects(html);
        var greeting = string.IsNullOrWhiteSpace(contactName) ? companyName : contactName.Trim();
        var tz = DemoLifecyclePlanner.TimeZoneFor(country);
        var endLocal = trialEndUtc is { } e ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e, DateTimeKind.Utc), tz) : (DateTime?)null;
        var daysLeft = trialEndUtc is { } e2 ? Math.Max(0, (int)Math.Ceiling((e2 - nowUtc).TotalDays)) : 0;
        var culture = DemoLifecyclePlanner.LanguageFor(country) switch { "de" => "de-DE", "fr" => "fr-FR", "nl" => "nl-NL", "es" => "es-ES", "it" => "it-IT", "pt" => "pt-PT", "tr" => "tr-TR", _ => "en-GB" };
        var endText = endLocal?.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo(culture)) ?? string.Empty;

        string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var body = html
            .Replace("{{CompanyName}}", Esc(companyName))
            .Replace("{{ContactName}}", Esc(greeting))
            .Replace("[Name]", Esc(greeting))
            .Replace("{{TrialEndDate}}", Esc(endText))
            .Replace("{{DaysLeft}}", daysLeft.ToString())
            .Replace("{{PortalUrl}}", portalUrl)
            .Replace("{{SupportEmail}}", Esc(supportEmail))
            .Replace("{{UnsubscribeUrl}}", unsubscribeUrl);
        body = System.Text.RegularExpressions.Regex.Replace(body, @"<!--\s*\d+\s*:\s*.*?-->\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        var subject = (subjects.Count > 0 ? subjects[0] : "LiftDesk")
            .Replace("{{CompanyName}}", companyName).Replace("{{ContactName}}", greeting).Replace("{{DaysLeft}}", daysLeft.ToString()).Replace("{{TrialEndDate}}", endText);
        return new Rendered(subject, body);
    }
}
