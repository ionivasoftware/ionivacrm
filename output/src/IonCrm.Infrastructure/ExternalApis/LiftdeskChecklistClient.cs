using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk company checklist API. Base URL + Bearer key are passed per call
/// (they live on the Liftdesk <c>Project</c> row). Responses are FLAT (no envelope); non-2xx
/// responses throw <see cref="HttpRequestException"/> with the Liftdesk body in the message so the
/// handlers can surface the short explanatory text (400 validation, 404 unknown company, 401 key).
/// </summary>
public sealed class LiftdeskChecklistClient : ILiftdeskChecklistClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LiftdeskChecklistClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskChecklistClient"/>.</summary>
    public LiftdeskChecklistClient(HttpClient httpClient, ILogger<LiftdeskChecklistClient> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    /// <inheritdoc />
    public async Task<LiftdeskChecklistDoc> GetChecklistAsync(
        string baseUrl, string apiKey, int companyId, string kind, int? type, int? culture,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Liftdesk checklist: fetching {Kind} checklist (type={Type}, culture={Culture}) for company {CompanyId}.",
            kind, type, culture, companyId);

        using var request = BuildRequest(HttpMethod.Get, baseUrl, apiKey,
            $"/api/v1/crm/companies/{companyId}/{kind}-checklist{ScopeQuery(type, culture)}");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<LiftdeskChecklistDoc>(JsonOpts, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response from Liftdesk checklist.");
    }

    /// <inheritdoc />
    public async Task<LiftdeskChecklistDoc> UpdateChecklistAsync(
        string baseUrl, string apiKey, int companyId, string kind, int? type, int? culture,
        LiftdeskChecklistUpdateRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Liftdesk checklist: updating {Kind} checklist (type={Type}, culture={Culture}) for company {CompanyId} ({HeaderCount} headers).",
            kind, type, culture, companyId, body.Headers.Count);

        using var request = BuildRequest(HttpMethod.Put, baseUrl, apiKey,
            $"/api/v1/crm/companies/{companyId}/{kind}-checklist{ScopeQuery(type, culture)}");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<LiftdeskChecklistDoc>(JsonOpts, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response from Liftdesk checklist update.");
    }

    /// <inheritdoc />
    public async Task<LiftdeskChecklistResetResponse> ResetChecklistsAsync(
        string baseUrl, string apiKey, int companyId, string kind, int? culture,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Liftdesk checklist: resetting {Kind} checklist(s) (culture={Culture}) to default for company {CompanyId}.",
            kind, culture, companyId);

        using var request = BuildRequest(HttpMethod.Post, baseUrl, apiKey,
            $"/api/v1/crm/companies/{companyId}/checklists/reset");
        request.Content = JsonContent.Create(new { kind, culture }, options: JsonOpts);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<LiftdeskChecklistResetResponse>(JsonOpts, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response from Liftdesk checklist reset.");
    }

    /// <summary>
    /// Builds the ?type=&amp;culture= scope. Both are optional; omitting one keeps Liftdesk's default
    /// for it (elevator / the company's own language).
    /// </summary>
    private static string ScopeQuery(int? type, int? culture)
    {
        var parts = new List<string>(2);
        if (type.HasValue) parts.Add($"type={type.Value}");
        if (culture.HasValue) parts.Add($"culture={culture.Value}");
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

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
    /// the status indicates failure, so the short explanatory Liftdesk error texts stay legible.
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
