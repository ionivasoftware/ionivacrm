using IonCrm.Application.Common.Helpers;

namespace IonCrm.Application.Features.SupportAccess;

/// <summary>
/// Public links in the consent mail and on the answer pages. Pure — unit-tested.
/// The host is configurable on its own (<c>SupportAccess:PublicBaseUrl</c>, e.g. a liftdesk.app subdomain that is a custom
/// domain of this API) so the customer never sees the Railway host; without it the API's shared public root is used.
/// The path is the short <c>/consent/{token}</c> alias rather than <c>/api/v1/support-access/{token}</c> (the old path still answers).
/// Form actions are absolute and come from the same root: behind Railway's proxy <c>Request.Scheme</c> reads "http" and the
/// edge answers plain http with a 301, which turns the browser's POST into a GET — the approve/decline buttons would never run.
/// </summary>
public static class SupportAccessLinks
{
    /// <summary>Short public route, used both as the controller alias and in the mail link.</summary>
    public const string ShortRoute = "consent";

    /// <summary><c>SupportAccess:PublicBaseUrl</c> → <c>SocialPosts:PublicApiBaseUrl</c> → Railway's public domain → null.</summary>
    public static string? ResolvePublicBaseUrl(Func<string, string?> config)
    {
        var own = config("SupportAccess:PublicBaseUrl");
        if (!string.IsNullOrWhiteSpace(own)) return own.Trim().TrimEnd('/');
        return SocialPostImageLinks.ResolvePublicApiBaseUrl(config);
    }

    public static string ReviewUrl(string baseUrl, string token) =>
        $"{baseUrl.Trim().TrimEnd('/')}/{ShortRoute}/{Uri.EscapeDataString(token)}";

    /// <summary>
    /// Root for the answer forms: the configured public root; otherwise the request's own origin as the proxy saw it
    /// (<c>X-Forwarded-Proto</c> wins over the internal scheme, so a TLS-terminating proxy still yields https).
    /// </summary>
    public static string RequestOrigin(string? configuredBase, string? forwardedProto, string scheme, string host)
    {
        if (!string.IsNullOrWhiteSpace(configuredBase)) return configuredBase.Trim().TrimEnd('/');
        var proto = string.IsNullOrWhiteSpace(forwardedProto) ? scheme : forwardedProto.Split(',')[0].Trim();
        return $"{(string.IsNullOrWhiteSpace(proto) ? "https" : proto)}://{host}";
    }
}
