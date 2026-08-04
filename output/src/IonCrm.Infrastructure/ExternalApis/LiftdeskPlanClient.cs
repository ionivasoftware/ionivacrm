using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk company subscription-plan API. Base URL + Bearer key are passed per
/// call (they live on the Liftdesk <c>Project</c> row). Responses are FLAT (no envelope); non-2xx
/// responses throw <see cref="HttpRequestException"/> carrying the Liftdesk body so the handlers can
/// surface its short explanatory text (400 validation, 404 unknown company/plan, 409 no subscription).
/// </summary>
public sealed class LiftdeskPlanClient : ILiftdeskPlanClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LiftdeskPlanClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskPlanClient"/>.</summary>
    public LiftdeskPlanClient(HttpClient httpClient, ILogger<LiftdeskPlanClient> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    /// <inheritdoc />
    public async Task<LiftdeskCompanyPlan> GetPlanAsync(
        string baseUrl, string apiKey, int companyId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Liftdesk plan: fetching plan for company {CompanyId}.", companyId);

        using var request = BuildRequest(HttpMethod.Get, baseUrl, apiKey, PlanPath(companyId));
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<LiftdeskCompanyPlan>(JsonOpts, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response from Liftdesk plan.");
    }

    /// <inheritdoc />
    public async Task<LiftdeskCompanyPlan> UpdatePlanAsync(
        string baseUrl, string apiKey, int companyId, LiftdeskPlanChangeRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Liftdesk plan: changing plan for company {CompanyId}. Tier={Tier} PlanId={PlanId} Period={Period}",
            companyId, body.Tier, body.PlanId, body.BillingPeriod);

        using var request = BuildRequest(HttpMethod.Put, baseUrl, apiKey, PlanPath(companyId));
        request.Content = JsonContent.Create(body, options: JsonOpts);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<LiftdeskCompanyPlan>(JsonOpts, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response from Liftdesk plan change.");
    }

    private static string PlanPath(int companyId) => $"/api/v1/crm/companies/{companyId}/plan";

    private static HttpRequestMessage BuildRequest(HttpMethod method, string baseUrl, string apiKey, string path)
    {
        var request = new HttpRequestMessage(method, $"{NormalizeBaseUrl(baseUrl)}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        return request;
    }

    /// <summary>Ensures the configured base URL has a scheme and no trailing slash, so
    /// <c>{base}/api/v1/crm/...</c> concatenation always yields a valid absolute URI.</summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }
        return trimmed.TrimEnd('/');
    }

    /// <summary>
    /// Throws <see cref="HttpRequestException"/> with the response body included in the message when
    /// the status indicates failure, keeping Liftdesk's short explanatory texts legible.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string body = string.Empty;
        try { body = await response.Content.ReadAsStringAsync(ct); } catch { /* ignore read failure */ }

        var detail = string.IsNullOrWhiteSpace(body)
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body.Trim()}";

        throw new HttpRequestException(detail, inner: null, statusCode: response.StatusCode);
    }
}
