using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis.CostProviders;

/// <summary>
/// Reports Anthropic's monthly cost. Two modes, checked in order:
///
/// 1. <b>Fixed subscription</b> — if <c>VendorCosts:Anthropic:MonthlyAmount</c> is set, that flat amount
///    is used every month. Use this when the Anthropic bill is a subscription (Claude Code / Pro-Max /
///    Team seats), which the Admin Cost API does NOT report (it only covers Developer Platform API token spend).
/// 2. <b>Live API usage</b> — otherwise, if <c>VendorCosts:Anthropic:AdminApiKey</c> is set, the monthly
///    spend is pulled from GET https://api.anthropic.com/v1/organizations/cost_report
///    (x-api-key = admin key sk-ant-admin01-…, anthropic-version: 2023-06-01). The API reports amounts as
///    decimal strings in the lowest currency unit (cents), so the summed daily buckets (across all pages)
///    are divided by 100 to get USD.
/// </summary>
public sealed class AnthropicCostProvider : ICostProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnthropicCostProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Initialises a new instance of <see cref="AnthropicCostProvider"/>.</summary>
    public AnthropicCostProvider(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicCostProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderKey => "Anthropic";

    private string? AdminKey => _configuration["VendorCosts:Anthropic:AdminApiKey"];
    private string? FixedAmountRaw => _configuration["VendorCosts:Anthropic:MonthlyAmount"];

    private bool HasFixedAmount =>
        decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) && amt > 0;

    /// <inheritdoc />
    public bool IsConfigured => HasFixedAmount || !string.IsNullOrWhiteSpace(AdminKey);

    /// <inheritdoc />
    public async Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        // Mode 1: fixed subscription amount (Claude Code / Pro-Max / Team) — the Cost API can't see these.
        if (decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fixedAmount) && fixedAmount > 0)
        {
            var fixedCurrency = _configuration["VendorCosts:Anthropic:Currency"];
            return new CostFetchResult(fixedAmount, string.IsNullOrWhiteSpace(fixedCurrency) ? "USD" : fixedCurrency);
        }

        // Mode 2: live Developer Platform API spend via the Admin Cost API.
        var key = AdminKey;
        if (string.IsNullOrWhiteSpace(key)) return null;

        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var endStr = end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        decimal totalCents = 0m;
        string? page = null;
        var guard = 0;

        do
        {
            var url = "https://api.anthropic.com/v1/organizations/cost_report"
                + $"?starting_at={Uri.EscapeDataString(startStr)}"
                + $"&ending_at={Uri.EscapeDataString(endStr)}"
                + "&bucket_width=1d&limit=31";
            if (!string.IsNullOrEmpty(page))
                url += $"&page={Uri.EscapeDataString(page)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("x-api-key", key);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic cost report {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            AnthropicCostReport? envelope;
            try { envelope = JsonSerializer.Deserialize<AnthropicCostReport>(body, JsonOpts); }
            catch (Exception ex) { _logger.LogWarning(ex, "Anthropic cost report parse failed."); return null; }

            if (envelope?.Data is not null)
            {
                foreach (var bucket in envelope.Data)
                {
                    if (bucket.Results is null) continue;
                    foreach (var r in bucket.Results)
                    {
                        if (decimal.TryParse(r.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var cents))
                            totalCents += cents;
                    }
                }
            }

            page = envelope?.HasMore == true ? envelope.NextPage : null;
        }
        while (!string.IsNullOrEmpty(page) && ++guard < 20);

        var usd = Math.Round(totalCents / 100m, 2, MidpointRounding.AwayFromZero);
        _logger.LogDebug("Anthropic cost for {Year}-{Month:D2}: {Usd} USD.", year, month, usd);
        return new CostFetchResult(usd, "USD");
    }
}

// ── Cost report response shape (snake_case) ──────────────────────────────────

internal sealed record AnthropicCostReport(
    [property: JsonPropertyName("data")] List<AnthropicCostBucket>? Data,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("next_page")] string? NextPage);

internal sealed record AnthropicCostBucket(
    [property: JsonPropertyName("results")] List<AnthropicCostResult>? Results);

internal sealed record AnthropicCostResult(
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("currency")] string? Currency);
