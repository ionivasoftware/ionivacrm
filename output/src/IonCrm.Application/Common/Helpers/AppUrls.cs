namespace IonCrm.Application.Common.Helpers;

/// <summary>Public addresses of the CRM's own frontend for browser redirects and links in alert mails.</summary>
public static class AppUrls
{
    /// <summary>
    /// Frontend origin without trailing slash: <c>App:FrontendUrl</c> (explicit, preferred), else the social publisher's
    /// <c>SocialPublisher:FrontendBaseUrl</c> which already points at the same app; null when neither is set. Deriving it
    /// from the CORS list would be fragile — the order of that list carries no meaning.
    /// </summary>
    public static string? FrontendBaseUrl(Func<string, string?> config)
    {
        foreach (var key in new[] { "App:FrontendUrl", "SocialPublisher:FrontendBaseUrl" })
        {
            var value = config(key)?.Trim();
            if (!string.IsNullOrEmpty(value) && !value.Any(char.IsControl)) return value.TrimEnd('/');
        }
        return null;
    }
}
