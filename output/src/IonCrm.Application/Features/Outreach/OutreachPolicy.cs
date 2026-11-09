using System.Text.RegularExpressions;

namespace IonCrm.Application.Features.Outreach;

/// <summary>
/// Country rules of the global outreach (09.11.2026 — Germany and Malta joined the lead agent):
/// which countries may receive the automatic cold e-mail sequence, and which template culture, site locale and
/// send-window time zone a lead's country gets. Pure, unit-tested.
/// </summary>
/// <remarks>
/// <see cref="DefaultEmailCountries"/>: unsolicited B2B e-mail to a corporate mailbox with a working opt-out is
/// lawful under the ePrivacy rules as transposed in the UK (PECR reg. 22 — corporate subscribers), Ireland
/// (S.I. 336/2011 — legal persons) and Malta (S.L. 586.01 — legal persons). Germany is deliberately NOT in the
/// list: UWG §7(2) Nr. 3 requires prior express consent even for B2B, so a DE lead is e-mailed only after the
/// operator confirms consent obtained by hand (phone / written) — <c>consentConfirmed</c> on approve/start.
/// <c>Outreach__EmailCountries</c> (CSV) overrides the default.
/// </remarks>
public static partial class OutreachPolicy
{
    public static readonly string[] DefaultEmailCountries = { "GB", "IE", "MT" };

    /// <summary>Default culture of the templates; a country without its own set falls back to this.</summary>
    public const string DefaultCulture = "en-GB";

    public static string Normalize(string? country)
    {
        var c = (country ?? string.Empty).Trim().ToUpperInvariant();
        return c == "UK" ? "GB" : c;
    }

    /// <summary>Configured CSV (<c>Outreach:EmailCountries</c>) or <see cref="DefaultEmailCountries"/>.</summary>
    public static IReadOnlySet<string> EmailCountries(string? configuredCsv)
    {
        var set = (configuredCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(c => c.Length == 2).ToHashSet(StringComparer.Ordinal);
        return set.Count > 0 ? set : DefaultEmailCountries.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>May the automatic sequence go to this country without a hand-confirmed consent?</summary>
    public static bool EmailAllowed(string? country, string? configuredCsv) => EmailCountries(configuredCsv).Contains(Normalize(country));

    /// <summary>Template culture: German-speaking countries get de-DE, everyone else the English set.</summary>
    public static string TemplateCulture(string? country) => Normalize(country) is "DE" or "AT" or "CH" ? "de-DE" : DefaultCulture;

    /// <summary>liftdesk.app locale folder: /uk/, /ie/, /de/, otherwise the international /en/ (Malta included).</summary>
    public static string SiteLocale(string? country) => Normalize(country) switch { "GB" => "uk", "IE" => "ie", "DE" => "de", _ => "en" };

    public static string SiteUrl(string? country) => $"https://liftdesk.app/{SiteLocale(country)}/";

    public static string SignupUrl(string? country) => $"https://liftdesk.app/{SiteLocale(country)}/signup/";

    /// <summary>Send window follows the recipient's clock.</summary>
    public static string TimeZoneId(string? country) => Normalize(country) switch
    {
        "DE" or "AT" or "CH" => "Europe/Berlin",
        "MT" => "Europe/Malta",
        _ => "Europe/London",
    };

    /// <summary>Operator-facing name used in Turkish messages.</summary>
    public static string CountryLabelTr(string? country) => Normalize(country) switch
    {
        "GB" => "Birleşik Krallık", "IE" => "İrlanda", "DE" => "Almanya", "MT" => "Malta", "AT" => "Avusturya", "CH" => "İsviçre",
        var c => c,
    };

    /// <summary>"intro.en-GB.html" → "intro.de-DE.html"; names without a culture suffix are returned unchanged.</summary>
    public static string ReCulture(string template, string culture) => CultureSuffix().Replace(template, $".{culture}$1");

    /// <summary>
    /// The sequence as it applies to one country: send window in the country's time zone, templates re-cultured
    /// where that file exists (otherwise the English template stays, so a missing translation never breaks a send).
    /// </summary>
    public static SequenceDefinition ForCountry(SequenceDefinition baseDef, string? country, Func<string, bool> templateExists)
    {
        var culture = TemplateCulture(country);
        var steps = baseDef.Steps.Select(s =>
        {
            if (culture == DefaultCulture) return s;
            var swapped = ReCulture(s.Template, culture);
            return swapped != s.Template && templateExists(swapped) ? s with { Template = swapped } : s;
        }).ToList();
        return baseDef with { TimeZone = TimeZoneId(country), Steps = steps };
    }

    /// <summary>Country-specific links: templates carry <c>{{SignupUrl}}</c> / <c>{{SiteUrl}}</c> instead of a fixed /en/ path.</summary>
    public static string ApplySitePlaceholders(string html, string? country) =>
        html.Replace("{{SignupUrl}}", SignupUrl(country)).Replace("{{SiteUrl}}", SiteUrl(country));

    [GeneratedRegex(@"\.[a-z]{2}-[A-Z]{2}(\.html)$")]
    private static partial Regex CultureSuffix();
}
