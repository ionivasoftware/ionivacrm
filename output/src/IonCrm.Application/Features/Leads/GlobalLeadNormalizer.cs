namespace IonCrm.Application.Features.Leads;

/// <summary>
/// Country-aware normalisation for non-TR leads. Names reuse <see cref="LeadNormalizer.NormalizeName"/>
/// (ASCII fold + legal suffixes) and additionally drop UK/IE legal forms (plc, llp, cic…) and German ones
/// (GmbH, GmbH &amp; Co. KG, UG, e.K., OHG, GbR, AG); phones become the national significant number
/// (UK: +44 20 7946 0958 → 2079460958; DE: +49 30 1234567 → 301234567; MT: +356 2123 4567 → 21234567).
/// Fingerprints are country-prefixed so a same-named firm in two countries never collides.
/// </summary>
public static class GlobalLeadNormalizer
{
    private static readonly HashSet<string> ExtraLegalTokens = new(StringComparer.Ordinal)
    {
        "plc", "llp", "lp", "cic", "uk", "gb", "ireland", "t", "a", "trading", "as",
        // Germany / Austria / Switzerland (folded: ü→u, ß→ss)
        "gmbh", "mbh", "kg", "ug", "haftungsbeschrankt", "ag", "ek", "ohg", "gbr", "cokg", "u", "und", "sohne", "sohn",
    };

    private static readonly Dictionary<string, string> DialCodes = new(StringComparer.OrdinalIgnoreCase) { ["GB"] = "44", ["IE"] = "353", ["DE"] = "49", ["AT"] = "43", ["CH"] = "41", ["MT"] = "356", ["FR"] = "33", ["BE"] = "32", ["NL"] = "31", ["ES"] = "34", ["PT"] = "351", ["IT"] = "39", ["US"] = "1" };

    public static string NormalizeCountry(string? raw)
    {
        var c = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (c is "UK") return "GB";
        return c.Length == 2 && c.All(char.IsLetter) ? c : "GB";
    }

    public static string NormalizeName(string? raw)
    {
        var tokens = LeadNormalizer.NormalizeName(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !ExtraLegalTokens.Contains(t));
        return string.Join(' ', tokens);
    }

    /// <summary>Digits only; strips 00/+ international prefix and the country's dial code, then a trunk 0. Empty when fewer than 7 digits remain.</summary>
    public static string NormalizePhone(string? raw, string country)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        var code = DialCodes.GetValueOrDefault(NormalizeCountry(country), "44");
        // +7: Malta numbers are 8 digits (+356 2123 4567) — the old +9 floor never stripped the 356.
        if (digits.StartsWith(code) && digits.Length >= code.Length + 7) digits = digits[code.Length..];
        if (digits.StartsWith('0') && digits.Length >= 10) digits = digits[1..];
        if (digits.Length < 7) return string.Empty;
        return digits.Length > 11 ? digits[^10..] : digits;
    }

    public static string Fingerprint(string country, string normalizedName, string? normalizedPhone, string? domain) =>
        LeadNormalizer.Fingerprint($"{NormalizeCountry(country).ToLowerInvariant()}:{normalizedName}", normalizedPhone, domain);

    /// <summary>Syntactic e-mail check plus role/junk filters that are useless for outreach (noreply, webmaster…).</summary>
    public static bool IsUsableEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var e = email.Trim();
        if (e.Length > 200 || e.Count(c => c == '@') != 1 || e.Contains(' ')) return false;
        var at = e.IndexOf('@');
        var local = e[..at].ToLowerInvariant(); var domain = e[(at + 1)..];
        if (local.Length == 0 || domain.Length < 4 || !domain.Contains('.') || domain.EndsWith('.')) return false;
        return local is not ("noreply" or "no-reply" or "donotreply" or "do-not-reply" or "webmaster" or "postmaster" or "abuse" or "privacy" or "dpo" or "unsubscribe");
    }
}
