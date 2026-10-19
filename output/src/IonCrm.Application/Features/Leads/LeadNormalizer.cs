using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IonCrm.Application.Features.Leads;

/// <summary>
/// Pure normalisation + matching for lead dedupe. Turkish-aware name folding (ç→c, ğ→g, ı/İ→i…),
/// legal-suffix stripping (Ltd. Şti., A.Ş., San. Tic.…), national 10-digit phone form, registrable
/// domain (generic mail providers ignored) and a stable fingerprint. Unit-tested.
/// </summary>
public static partial class LeadNormalizer
{
    private static readonly HashSet<string> LegalTokens = new(StringComparer.Ordinal)
    {
        "ltd", "sti", "as", "a.s", "san", "tic", "sanayi", "ticaret", "limited", "sirketi", "sirket", "anonim", "ve", "ins", "insaat",
        "muh", "muhendislik", "hizmetleri", "hizmet", "hiz", "paz", "pazarlama", "ith", "ihr", "ithalat", "ihracat", "elektrik", "elk", "mak", "makina", "makine",
        "koll", "kollektif", "ortakligi", "sahis", "sahsi", "firmasi", "firma", "ltdsti", "the", "co", "company", "inc",
    };

    private static readonly HashSet<string> GenericMailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "hotmail.com", "hotmail.com.tr", "outlook.com", "outlook.com.tr", "yahoo.com", "yahoo.com.tr", "icloud.com", "yandex.com",
        "yandex.com.tr", "mynet.com", "live.com", "msn.com", "windowslive.com", "superonline.com", "ttmail.com", "gmx.com", "protonmail.com",
    };

    /// <summary>Digits only; strips +90 / 0090 / leading 0; returns the national 10-digit number (or the digits when shorter), empty when &lt; 7 digits.</summary>
    public static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0090")) digits = digits[4..];
        else if (digits.StartsWith("90") && digits.Length >= 12) digits = digits[2..];
        if (digits.StartsWith('0') && digits.Length >= 11) digits = digits[1..];
        if (digits.Length < 7) return string.Empty;
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>ASCII-folded, lower-case company name without punctuation and legal suffixes.</summary>
    public static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var folded = Fold(raw).Replace(".", string.Empty);
        var cleaned = NonWord().Replace(folded, " ");
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !LegalTokens.Contains(t)).ToList();
        return string.Join(' ', tokens);
    }

    /// <summary>Lower-case ASCII fold with Turkish letters mapped explicitly (İ→i, I→i, ı→i, ş→s…).</summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case 'İ': case 'I': case 'ı': case 'i': sb.Append('i'); break;
                case 'Ş': case 'ş': sb.Append('s'); break;
                case 'Ğ': case 'ğ': sb.Append('g'); break;
                case 'Ç': case 'ç': sb.Append('c'); break;
                case 'Ö': case 'ö': sb.Append('o'); break;
                case 'Ü': case 'ü': sb.Append('u'); break;
                default:
                    var lower = char.ToLowerInvariant(ch);
                    var decomposed = lower.ToString().Normalize(NormalizationForm.FormD);
                    foreach (var c in decomposed)
                        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Registrable host from a URL or e-mail; null for empty input or generic mail providers.</summary>
    public static string? DomainOf(string? websiteOrEmail)
    {
        if (string.IsNullOrWhiteSpace(websiteOrEmail)) return null;
        var s = websiteOrEmail.Trim().ToLowerInvariant();
        if (s.Contains('@')) s = s[(s.LastIndexOf('@') + 1)..];
        s = Regex.Replace(s, @"^[a-z]+://", "");
        s = s.Split('/', '?', '#')[0].Split(':')[0];
        if (s.StartsWith("www.")) s = s[4..];
        if (s.Length < 4 || !s.Contains('.')) return null;
        return GenericMailDomains.Contains(s) ? null : s;
    }

    /// <summary>Stable identity of a candidate: name + phone + domain (name alone when the others are missing).</summary>
    public static string Fingerprint(string normalizedName, string? normalizedPhone, string? domain)
    {
        var material = $"{normalizedName}|{normalizedPhone ?? string.Empty}|{domain ?? string.Empty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Sector-generic words that do not distinguish one elevator company from another.</summary>
    private static readonly HashSet<string> GenericTokens = new(StringComparer.Ordinal)
    {
        "asansor", "asansorleri", "asansorcu", "lift", "lifts", "elevator", "elevators", "yuruyen", "merdiven", "sistemleri", "sistem", "sistemler",
        "servis", "servisi", "bakim", "bakimi", "onarim", "montaj", "montaji", "teknik", "teknoloji", "teknolojileri", "grup", "group", "proje", "dizayn",
    };

    /// <summary>
    /// 0–1 similarity of two normalised names. Base: max(token Jaccard, Levenshtein ratio). On top, the
    /// DISTINCTIVE tokens (sector words like "asansor", "servis" removed) are compared: a fully contained
    /// distinctive set ("yildiz" ⊂ "yildiz ozkan") scores ≥ 0.85 so near-identical brands surface even when
    /// one side carries extra words — while "yildiz asansor" vs "kartal asansor" stays low.
    /// </summary>
    public static double NameSimilarity(string normalizedA, string normalizedB)
    {
        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB)) return 0;
        if (normalizedA == normalizedB) return 1;
        var ta = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var jaccard = ta.Count + tb.Count == 0 ? 0 : (double)ta.Intersect(tb).Count() / ta.Union(tb).Count();
        var lev = 1.0 - (double)Levenshtein(normalizedA, normalizedB) / Math.Max(normalizedA.Length, normalizedB.Length);
        var score = Math.Max(jaccard, lev);

        var da = ta.Where(t => !GenericTokens.Contains(t)).ToHashSet();
        var db = tb.Where(t => !GenericTokens.Contains(t)).ToHashSet();
        if (da.Count > 0 && db.Count > 0)
        {
            var inter = da.Intersect(db).Count();
            var containment = (double)inter / Math.Min(da.Count, db.Count);
            var jaccardD = (double)inter / da.Union(db).Count();
            score = Math.Max(score, containment * (0.75 + 0.25 * jaccardD));
        }
        return score;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}

/// <summary>A known company (customer or earlier candidate) in normalised form, for matching.</summary>
public record KnownCompany(Guid Id, string Name, string NormalizedName, string? NormalizedPhone, string? Domain, string? TaxNumber, string? City);

/// <summary>Result of comparing a new candidate against known companies.</summary>
public record DedupeVerdict(bool IsDuplicate, Guid? MatchId, string? MatchName, Guid? PossibleMatchId, string? PossibleMatchName, string Notes);

public static class LeadDedupe
{
    public const double DuplicateThreshold = 0.92;
    public const double PossibleThreshold = 0.80;

    /// <summary>Strong signals (tax no, phone, domain, identical name, ≥0.92 name) → duplicate; 0.80–0.92 → possible match, human decides.</summary>
    public static DedupeVerdict Evaluate(string normalizedName, string? normalizedPhone, string? domain, string? taxNumber, string? city, IEnumerable<KnownCompany> known)
    {
        KnownCompany? possible = null; double possibleScore = 0;
        foreach (var k in known)
        {
            if (!string.IsNullOrWhiteSpace(taxNumber) && !string.IsNullOrWhiteSpace(k.TaxNumber) && taxNumber.Trim() == k.TaxNumber.Trim())
                return new(true, k.Id, k.Name, null, null, $"Vergi no eşleşti ({k.Name})");
            if (!string.IsNullOrEmpty(normalizedPhone) && normalizedPhone.Length >= 10 && normalizedPhone == k.NormalizedPhone)
                return new(true, k.Id, k.Name, null, null, $"Telefon eşleşti ({k.Name})");
            if (!string.IsNullOrEmpty(domain) && string.Equals(domain, k.Domain, StringComparison.OrdinalIgnoreCase))
                return new(true, k.Id, k.Name, null, null, $"Web alanı eşleşti ({k.Name})");
            if (normalizedName.Length > 0 && normalizedName == k.NormalizedName)
                return new(true, k.Id, k.Name, null, null, $"Aynı isim ({k.Name})");

            var sim = LeadNormalizer.NameSimilarity(normalizedName, k.NormalizedName);
            // Same province makes a near-identical name much more likely the same firm.
            var sameCity = !string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(k.City) && LeadNormalizer.Fold(city) == LeadNormalizer.Fold(k.City);
            if (sim >= DuplicateThreshold || (sim >= 0.86 && sameCity))
                return new(true, k.Id, k.Name, null, null, $"İsim çok benzer: {k.Name} ({sim:P0}{(sameCity ? ", aynı il" : "")})");
            if (sim >= PossibleThreshold && sim > possibleScore) { possible = k; possibleScore = sim; }
        }
        return possible is null
            ? new(false, null, null, null, null, string.Empty)
            : new(false, null, null, possible.Id, possible.Name, $"Olası eşleşme: {possible.Name} ({possibleScore:P0}) — kontrol edin");
    }
}
