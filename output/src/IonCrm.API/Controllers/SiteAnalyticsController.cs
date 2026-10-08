using System.Text.RegularExpressions;
using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// liftdesk.app kullanım verisi (docs/crm-site-analytics-api.md). Liftdesk'teki salt okunur uçları
/// proxy'ler; statik CRM anahtarı sunucuda kalır. SuperAdmin'e açık (şirket geneli pazarlama verisi).
/// </summary>
[Route("api/v1/site-analytics")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SiteAnalyticsController : ApiControllerBase
{
    private static readonly Regex DateRe    = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex CountryRe = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Sites = new(StringComparer.OrdinalIgnoreCase) { "tr", "en", "uk" };
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
        { "pageview", "section_view", "click", "scroll", "signup_start", "signup_verified" };

    private readonly ILiftdeskSiteAnalyticsClient _client;
    public SiteAnalyticsController(ILiftdeskSiteAnalyticsClient client) => _client = client;

    /// <summary>GET /api/v1/site-analytics/overview?from=&amp;to=&amp;site=&amp;country=</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSiteAnalyticsOverview>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? site, [FromQuery] string? country,
        CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));
        if (Validate(from, to, site, country) is { } err)
            return BadRequest(ApiResponse<object>.Fail(err, 400));

        var env = await _client.GetOverviewAsync(from, to, Norm(site), country?.ToUpperInvariant(), cancellationToken);
        if (!env.Success || env.Data is null)
            return BadRequest(ApiResponse<object>.Fail(env.Message ?? "Site verisi alınamadı.", 400));
        return OkResponse(env.Data);
    }

    /// <summary>GET /api/v1/site-analytics/events?from=&amp;to=&amp;site=&amp;country=&amp;type=&amp;limit=</summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(ApiResponse<List<LiftdeskSiteAnalyticsEvent>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? site, [FromQuery] string? country,
        [FromQuery] string? type, [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));
        if (Validate(from, to, site, country) is { } err)
            return BadRequest(ApiResponse<object>.Fail(err, 400));

        limit = Math.Clamp(limit, 1, 500);
        var t = string.IsNullOrWhiteSpace(type) || !Types.Contains(type) ? null : type.ToLowerInvariant();

        var env = await _client.GetEventsAsync(from, to, Norm(site), country?.ToUpperInvariant(), t, limit, cancellationToken);
        if (!env.Success)
            return BadRequest(ApiResponse<object>.Fail(env.Message ?? "Olaylar alınamadı.", 400));
        return OkResponse(env.Data ?? new List<LiftdeskSiteAnalyticsEvent>());
    }

    private static string? Norm(string? site) => string.IsNullOrWhiteSpace(site) ? null : site.ToLowerInvariant();

    private static string? Validate(string? from, string? to, string? site, string? country)
    {
        if (!string.IsNullOrWhiteSpace(from) && !DateRe.IsMatch(from)) return "from yyyy-MM-dd olmalı.";
        if (!string.IsNullOrWhiteSpace(to) && !DateRe.IsMatch(to))     return "to yyyy-MM-dd olmalı.";
        if (!string.IsNullOrWhiteSpace(site) && !Sites.Contains(site)) return "site tr | en | uk olmalı.";
        if (!string.IsNullOrWhiteSpace(country) && !CountryRe.IsMatch(country)) return "country ISO alfa-2 olmalı (ör. GB).";
        return null;
    }
}
