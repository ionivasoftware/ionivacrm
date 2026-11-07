namespace IonCrm.Application.Common.Helpers;

/// <summary>
/// Which country a customer belongs to for reporting: the signup country from the SaaS feed when known
/// (<c>Customer.Country</c>), otherwise the international calling code of its phone number. A nationally
/// written number ("0555 …") says nothing about the country; whatever neither source resolves is counted as the
/// home market <see cref="DefaultCountry"/> (TR) — every such firm in the list is Turkish (2026-11-07), there is no
/// "unknown" bucket. Reporting only: the demo lifecycle mails still read <c>Customer.Country</c> directly.
/// </summary>
public static class CountryInference
{
    /// <summary>Country came from the customer record (feed).</summary>
    public const string SourceRecord = "record";

    /// <summary>Country was derived from the phone number's international prefix.</summary>
    public const string SourcePhone = "phone";

    /// <summary>Neither the record nor the phone told; counted as <see cref="DefaultCountry"/>.</summary>
    public const string SourceDefault = "default";

    /// <summary>Home market: a firm with no country and no internationally written phone is Turkish.</summary>
    public const string DefaultCountry = "TR";

    // Longest-prefix match: 3-digit codes are tried before 2-digit, then 1-digit.
    // +1 is read as US (Canada shares it); +7 as RU (Kazakhstan shares it).
    private static readonly Dictionary<string, string> ByCallingCode = new()
    {
        ["1"] = "US", ["7"] = "RU",
        ["20"] = "EG", ["27"] = "ZA", ["30"] = "GR", ["31"] = "NL", ["32"] = "BE", ["33"] = "FR", ["34"] = "ES", ["36"] = "HU", ["39"] = "IT",
        ["40"] = "RO", ["41"] = "CH", ["43"] = "AT", ["44"] = "GB", ["45"] = "DK", ["46"] = "SE", ["47"] = "NO", ["48"] = "PL", ["49"] = "DE",
        ["51"] = "PE", ["52"] = "MX", ["54"] = "AR", ["55"] = "BR", ["56"] = "CL", ["57"] = "CO",
        ["60"] = "MY", ["61"] = "AU", ["62"] = "ID", ["63"] = "PH", ["64"] = "NZ", ["65"] = "SG", ["66"] = "TH",
        ["81"] = "JP", ["82"] = "KR", ["84"] = "VN", ["86"] = "CN", ["90"] = "TR", ["91"] = "IN", ["92"] = "PK", ["93"] = "AF", ["94"] = "LK", ["95"] = "MM", ["98"] = "IR",
        ["212"] = "MA", ["213"] = "DZ", ["216"] = "TN", ["218"] = "LY", ["234"] = "NG", ["254"] = "KE",
        ["351"] = "PT", ["352"] = "LU", ["353"] = "IE", ["354"] = "IS", ["355"] = "AL", ["356"] = "MT", ["357"] = "CY", ["358"] = "FI", ["359"] = "BG",
        ["370"] = "LT", ["371"] = "LV", ["372"] = "EE", ["373"] = "MD", ["374"] = "AM", ["375"] = "BY", ["377"] = "MC",
        ["380"] = "UA", ["381"] = "RS", ["382"] = "ME", ["383"] = "XK", ["385"] = "HR", ["386"] = "SI", ["387"] = "BA", ["389"] = "MK",
        ["420"] = "CZ", ["421"] = "SK", ["880"] = "BD",
        ["961"] = "LB", ["962"] = "JO", ["963"] = "SY", ["964"] = "IQ", ["965"] = "KW", ["966"] = "SA", ["967"] = "YE", ["968"] = "OM",
        ["971"] = "AE", ["972"] = "IL", ["973"] = "BH", ["974"] = "QA", ["977"] = "NP",
        ["992"] = "TJ", ["993"] = "TM", ["994"] = "AZ", ["995"] = "GE", ["996"] = "KG", ["998"] = "UZ",
    };

    /// <summary>ISO 3166-1 alpha-2 upper-case, or null for anything that is not a two-letter code.</summary>
    public static string? Normalize(string? country)
    {
        var c = country?.Trim().ToUpperInvariant();
        return c is { Length: 2 } && c.All(char.IsLetter) ? c : null;
    }

    /// <summary>
    /// Country from an internationally written phone number ("+44 20 …", "0049 …", "00 90 555 …"); null for
    /// national formats and unknown prefixes.
    /// </summary>
    public static string? FromPhone(string? phone)
    {
        var p = phone?.Trim();
        if (string.IsNullOrEmpty(p)) return null;
        var digits = new string(p.Where(char.IsDigit).ToArray());
        var intl = p.StartsWith('+') ? digits : p.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : null;
        if (intl is null || intl.Length < 8) return null;
        for (var len = 3; len >= 1; len--)
            if (intl.Length >= len && ByCallingCode.TryGetValue(intl[..len], out var iso)) return iso;
        return null;
    }

    /// <summary>Record country first, phone prefix second, <see cref="DefaultCountry"/> when neither tells.</summary>
    public static (string Country, string Source) Resolve(string? country, string? phone)
    {
        if (Normalize(country) is { } c) return (c, SourceRecord);
        if (FromPhone(phone) is { } p) return (p, SourcePhone);
        return (DefaultCountry, SourceDefault);
    }
}
