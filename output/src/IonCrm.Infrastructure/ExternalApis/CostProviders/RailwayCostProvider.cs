using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis.CostProviders;

/// <summary>
/// Reports Railway's monthly cost. Two modes, checked in order:
///
/// 1. <b>Live GraphQL</b> — when <c>ApiToken</c> is set, queries
///    https://backboard.railway.com/graphql/v2 (Authorization: Bearer token) for the running total of
///    the current invoice: <c>me.workspaces[].customer.subscriptions[].nextInvoiceCurrentTotal</c>.
///    Railway/Stripe amounts are in <b>cents</b>, so the summed total is divided by 100.
///    Because this is the *current cycle's* accruing total (not a per-calendar-month figure), it is
///    only reported for the current calendar month; other months return null so the daily job leaves
///    their previously-captured values intact. Optionally scoped to one workspace via <c>WorkspaceId</c>.
/// 2. <b>Fixed amount</b> — when no token is set, falls back to <c>VendorCosts:Railway:MonthlyAmount</c>.
///
/// Config keys (under <c>VendorCosts:Railway</c>): ApiToken, WorkspaceId (optional), MonthlyAmount, Currency.
/// </summary>
public sealed class RailwayCostProvider : ICostProvider
{
    private const string GraphQlEndpoint = "https://backboard.railway.com/graphql/v2";
    private const string CostQuery =
        "query{me{workspaces{id customer{subscriptions{nextInvoiceCurrentTotal status}}}}}";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RailwayCostProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Initialises a new instance of <see cref="RailwayCostProvider"/>.</summary>
    public RailwayCostProvider(HttpClient httpClient, IConfiguration configuration, ILogger<RailwayCostProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderKey => "Railway";

    private string? ApiToken => _configuration["VendorCosts:Railway:ApiToken"];
    private string? WorkspaceId => _configuration["VendorCosts:Railway:WorkspaceId"];
    private string? FixedAmountRaw => _configuration["VendorCosts:Railway:MonthlyAmount"];

    private bool HasFixedAmount =>
        decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) && amt > 0;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken) || HasFixedAmount;

    private string Currency
    {
        get
        {
            var c = _configuration["VendorCosts:Railway:Currency"];
            return string.IsNullOrWhiteSpace(c) ? "USD" : c;
        }
    }

    /// <inheritdoc />
    public async Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        // Live mode: authoritative for Railway. Only the current calendar month gets the accruing total.
        if (!string.IsNullOrWhiteSpace(ApiToken))
        {
            var now = DateTime.UtcNow;
            if (year != now.Year || month != now.Month)
                return null; // not the current cycle — leave the previously-captured value

            return await QueryGraphQlAsync(cancellationToken);
        }

        // Fixed fallback (no token).
        if (decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fixedAmount) && fixedAmount > 0)
            return new CostFetchResult(fixedAmount, Currency);

        return null;
    }

    private async Task<CostFetchResult?> QueryGraphQlAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiToken}");
            request.Content = JsonContent.Create(new { query = CostQuery });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Railway GraphQL {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                _logger.LogWarning("Railway GraphQL errors: {Errors}", errors.ToString());
                return null;
            }

            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("me", out var me)
                || !me.TryGetProperty("workspaces", out var workspaces)
                || workspaces.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Railway GraphQL: unexpected response shape. Body: {Body}", body);
                return null;
            }

            decimal totalCents = 0m;
            var wanted = WorkspaceId;

            foreach (var ws in workspaces.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(wanted)
                    && (!ws.TryGetProperty("id", out var wsId) || wsId.GetString() != wanted))
                    continue;

                if (!ws.TryGetProperty("customer", out var customer) || customer.ValueKind != JsonValueKind.Object) continue;
                if (!customer.TryGetProperty("subscriptions", out var subs) || subs.ValueKind != JsonValueKind.Array) continue;

                foreach (var sub in subs.EnumerateArray())
                {
                    // Skip cancelled subscriptions; count active/past_due/trialing.
                    if (sub.TryGetProperty("status", out var status)
                        && string.Equals(status.GetString(), "canceled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (sub.TryGetProperty("nextInvoiceCurrentTotal", out var total) && total.ValueKind == JsonValueKind.Number)
                        totalCents += total.GetDecimal();
                }
            }

            var amount = Math.Round(totalCents / 100m, 2, MidpointRounding.AwayFromZero);
            _logger.LogDebug("Railway current-cycle total: {Amount} {Currency}.", amount, Currency);
            return new CostFetchResult(amount, Currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Railway GraphQL cost query failed.");
            return null;
        }
    }
}
