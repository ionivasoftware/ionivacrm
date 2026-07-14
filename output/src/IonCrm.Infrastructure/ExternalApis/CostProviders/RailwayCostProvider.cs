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
///    https://backboard.railway.com/graphql/v2 (Authorization: Bearer token). Railway/Stripe amounts are
///    in <b>cents</b>, so totals are divided by 100. Two cases by period:
///    <list type="bullet">
///      <item><b>Current calendar month</b> → the accruing running total of the open invoice:
///        <c>customer.subscriptions[].nextInvoiceCurrentTotal</c>.</item>
///      <item><b>Past month</b> → the finalised invoice whose <c>periodStart</c> falls in that calendar
///        month: <c>customer.invoices[].amountDue</c>. Railway bills on a mid-month cycle (e.g. the 9th),
///        so an invoice is attributed to the calendar month its billing period starts in.</item>
///    </list>
///    Optionally scoped to one workspace via <c>WorkspaceId</c>.
/// 2. <b>Fixed amount</b> — when no token is set, falls back to <c>VendorCosts:Railway:MonthlyAmount</c>.
///
/// Config keys (under <c>VendorCosts:Railway</c>): ApiToken, WorkspaceId (optional), MonthlyAmount, Currency.
/// </summary>
public sealed class RailwayCostProvider : ICostProvider
{
    private const string GraphQlEndpoint = "https://backboard.railway.com/graphql/v2";
    private const string CostQuery =
        "query{me{workspaces{id customer{" +
        "subscriptions{nextInvoiceCurrentTotal status}" +
        "invoices{amountDue periodStart status}" +
        "}}}}";

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
        // Live mode is authoritative for Railway when a token is present.
        if (!string.IsNullOrWhiteSpace(ApiToken))
            return await QueryGraphQlAsync(year, month, cancellationToken);

        // Fixed fallback (no token).
        if (decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fixedAmount) && fixedAmount > 0)
            return new CostFetchResult(fixedAmount, Currency);

        return null;
    }

    private async Task<CostFetchResult?> QueryGraphQlAsync(int year, int month, CancellationToken cancellationToken)
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

            var now = DateTime.UtcNow;
            var isCurrentMonth = year == now.Year && month == now.Month;
            var wanted = WorkspaceId;

            decimal totalCents = 0m;
            var matched = false;

            foreach (var ws in workspaces.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(wanted)
                    && (!ws.TryGetProperty("id", out var wsId) || wsId.GetString() != wanted))
                    continue;

                if (!ws.TryGetProperty("customer", out var customer) || customer.ValueKind != JsonValueKind.Object) continue;

                if (isCurrentMonth)
                {
                    // Current calendar month → running total of the open invoice.
                    if (customer.TryGetProperty("subscriptions", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sub in subs.EnumerateArray())
                        {
                            if (sub.TryGetProperty("status", out var status)
                                && string.Equals(status.GetString(), "canceled", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (sub.TryGetProperty("nextInvoiceCurrentTotal", out var total) && total.ValueKind == JsonValueKind.Number)
                            {
                                totalCents += total.GetDecimal();
                                matched = true;
                            }
                        }
                    }
                }
                else
                {
                    // Past month → finalised invoice(s) whose billing period starts in that calendar month.
                    if (customer.TryGetProperty("invoices", out var invoices) && invoices.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var inv in invoices.EnumerateArray())
                        {
                            if (inv.TryGetProperty("status", out var st)
                                && string.Equals(st.GetString(), "void", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!inv.TryGetProperty("periodStart", out var ps)
                                || !DateTimeOffset.TryParse(ps.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
                                continue;
                            var startUtc = start.UtcDateTime;
                            if (startUtc.Year != year || startUtc.Month != month) continue;
                            if (inv.TryGetProperty("amountDue", out var due) && due.ValueKind == JsonValueKind.Number)
                            {
                                totalCents += due.GetDecimal();
                                matched = true;
                            }
                        }
                    }
                }
            }

            if (!matched) return null; // no data for this month — leave it untouched

            var amount = Math.Round(totalCents / 100m, 2, MidpointRounding.AwayFromZero);
            _logger.LogDebug("Railway cost for {Year}-{Month:D2}: {Amount} {Currency} ({Mode}).",
                year, month, amount, Currency, isCurrentMonth ? "running" : "invoice");
            return new CostFetchResult(amount, Currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Railway GraphQL cost query failed for {Year}-{Month:D2}.", year, month);
            return null;
        }
    }
}
