using System.Globalization;
using System.Text;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Company-name normalization for the EMS→Liftdesk name-based migration.
///
/// Production Liftdesk company ids do NOT match the retired EMS ids (learned the hard way on
/// 2026-08-30: the id-based migration paired 462 of 564 rows with the wrong firm and had to be
/// rolled back), so the only reliable join key between the two datasets is the company name.
///
/// Two levels of normalization:
/// <list type="bullet">
/// <item><see cref="Normalize"/> — Turkish characters folded to ASCII, lower-cased, punctuation
/// stripped, whitespace collapsed. "Elko Asansör San. ve Tic. Ltd. Şti" and
/// "elko asansör san.ve tic.ltd.şti." normalize identically.</item>
/// <item><see cref="Core"/> — <see cref="Normalize"/> plus generic industry/legal-form tokens
/// (asansör, ltd, şti, san, tic, …) and single-letter tokens removed, leaving the distinctive
/// part of the name. "Mega Asansör" and "MEGA ASANSÖR ELEKTRİK SANAYİ VE TİCARET LTD ŞTİ" share
/// the core "mega". Empty when the whole name is generic — callers must skip empty cores.</item>
/// </list>
/// </summary>
public static class CompanyNameMatcher
{
    /// <summary>Generic tokens that carry no identity: sector words + legal-form abbreviations.</summary>
    private static readonly HashSet<string> StopTokens = new(StringComparer.Ordinal)
    {
        "asansor", "ltd", "sti", "ltdsti", "san", "tic", "ve", "as",
        "insaat", "muhendislik", "elektrik", "elektronik", "sanayi", "ticaret",
        "limited", "sirketi", "hizmetleri", "muh", "bakim", "servis", "yedek", "parca",
    };

    /// <summary>Folds Turkish letters, lower-cases, strips punctuation/accents, collapses spaces.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var sb = new StringBuilder(name.Length);
        foreach (var raw in name)
        {
            // Fold Turkish letters BEFORE lower-casing: invariant ToLower of 'İ' (U+0130) yields
            // "i" + combining dot, and 'I' must become 'i' (not dotless 'ı') for matching.
            var ch = raw switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => raw,
            };
            sb.Append(char.ToLowerInvariant(ch));
        }

        // Strip any remaining accents (â → a, é → e, …) via canonical decomposition.
        var decomposed = sb.ToString().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSpace && result.Length > 0) result.Append(' ');
                pendingSpace = false;
                result.Append(ch);
            }
            else
            {
                pendingSpace = true; // punctuation & whitespace both collapse to one separator
            }
        }
        return result.ToString();
    }

    /// <summary>Distinctive part of the name: <see cref="Normalize"/> minus generic tokens.</summary>
    public static string Core(string? name)
    {
        var normalized = Normalize(name);
        if (normalized.Length == 0) return string.Empty;

        var kept = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !StopTokens.Contains(t));
        return string.Join(' ', kept);
    }
}
