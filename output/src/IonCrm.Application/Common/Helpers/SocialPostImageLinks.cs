using System.Text.RegularExpressions;

namespace IonCrm.Application.Common.Helpers;

/// <summary>
/// Builds and recognises the CRM's own public image links for social-media posts
/// (<c>{base}/api/v1/social-posts/images/{id}</c>). Recognising them matters for cleanup: when an
/// operator replaces or clears a custom image, the old blob is removed only if the URL is ours —
/// a hand-pasted external URL is never touched. The publisher uses the same links for template
/// cards it stores for Instagram (which fetches images from a public URL).
/// </summary>
public static partial class SocialPostImageLinks
{
    public const string PathPrefix = "/api/v1/social-posts/images/";

    public static string Build(string publicApiBaseUrl, Guid imageId) =>
        $"{publicApiBaseUrl.TrimEnd('/')}{PathPrefix}{imageId:D}";

    /// <summary>Extracts the image id from one of our own links; null for anything else.</summary>
    public static Guid? TryParseImageId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var m = OwnLink().Match(url);
        return m.Success && Guid.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"/api/v1/social-posts/images/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:[?#]|$)")]
    private static partial Regex OwnLink();

    /// <summary>
    /// Sniffs the real image type from the first bytes — the browser-supplied content type is not
    /// trusted. Returns null when the bytes are not PNG / JPEG / WebP.
    /// </summary>
    public static string? SniffImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "image/webp";
        return null;
    }

    /// <summary>
    /// Public root of this API for links handed to third parties. Explicit config wins
    /// (<c>SocialPosts:PublicApiBaseUrl</c>); otherwise Railway's injected public domain. Null when
    /// neither exists — callers must then fail loudly rather than hand out an unreachable URL.
    /// </summary>
    public static string? ResolvePublicApiBaseUrl(Func<string, string?> config)
    {
        var configured = config("SocialPosts:PublicApiBaseUrl");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
        var railway = config("RAILWAY_PUBLIC_DOMAIN");
        return string.IsNullOrWhiteSpace(railway) ? null : $"https://{railway.Trim().TrimEnd('/')}";
    }
}
