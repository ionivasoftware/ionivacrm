using FluentAssertions;
using IonCrm.Application.Features.SupportAccess;

namespace IonCrm.Tests.SupportAccess;

public class SupportAccessLinksTests
{
    private static Func<string, string?> Config(params (string Key, string? Value)[] pairs) =>
        key => pairs.FirstOrDefault(p => p.Key == key).Value;

    [Fact]
    public void Own_public_root_wins_over_the_shared_api_root_and_railway_domain()
    {
        SupportAccessLinks.ResolvePublicBaseUrl(Config(("SupportAccess:PublicBaseUrl", "https://destek.liftdesk.app/ "), ("SocialPosts:PublicApiBaseUrl", "https://crm-api"), ("RAILWAY_PUBLIC_DOMAIN", "x.up.railway.app")))
            .Should().Be("https://destek.liftdesk.app");
        SupportAccessLinks.ResolvePublicBaseUrl(Config(("SupportAccess:PublicBaseUrl", "  "), ("SocialPosts:PublicApiBaseUrl", "https://crm-api/"), ("RAILWAY_PUBLIC_DOMAIN", "x.up.railway.app")))
            .Should().Be("https://crm-api");
        SupportAccessLinks.ResolvePublicBaseUrl(Config(("RAILWAY_PUBLIC_DOMAIN", "x.up.railway.app"))).Should().Be("https://x.up.railway.app");
        SupportAccessLinks.ResolvePublicBaseUrl(Config()).Should().BeNull();
    }

    [Fact]
    public void Review_link_uses_the_short_consent_path_and_escapes_the_token()
    {
        SupportAccessLinks.ReviewUrl("https://destek.liftdesk.app/", "abc_-1").Should().Be("https://destek.liftdesk.app/consent/abc_-1");
        SupportAccessLinks.ReviewUrl("https://crm", "a b/c").Should().Be("https://crm/consent/a%20b%2Fc");
        SupportAccessLinks.ReviewUrl("https://crm", SupportAccessRules.NewToken()).Should().NotContain("%");
    }

    [Fact]
    public void Form_root_is_the_configured_root_or_the_origin_the_proxy_saw()
    {
        // Configured root wins regardless of which host the customer opened the page on.
        SupportAccessLinks.RequestOrigin("https://destek.liftdesk.app", "https", "http", "ion-crm-api-production.up.railway.app").Should().Be("https://destek.liftdesk.app");
        // Behind a TLS-terminating proxy the internal scheme is http; the forwarded proto must win (a http form action 301s and loses the POST).
        SupportAccessLinks.RequestOrigin(null, "https", "http", "ion-crm-api-production.up.railway.app").Should().Be("https://ion-crm-api-production.up.railway.app");
        SupportAccessLinks.RequestOrigin(null, "https, http", "http", "crm").Should().Be("https://crm");
        // Local development without a proxy keeps its own scheme.
        SupportAccessLinks.RequestOrigin(null, null, "http", "localhost:5000").Should().Be("http://localhost:5000");
        SupportAccessLinks.RequestOrigin(null, "", "", "crm").Should().Be("https://crm");
    }
}
