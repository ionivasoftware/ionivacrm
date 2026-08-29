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
///    in <b>cents</b>, so totals are divided by 100.
///
///    Railway bills on a mid-month anchor day (workspace-configurable, defaults to 8): a cycle running
///    <c>8 May → 8 Jun</c> is invoiced <b>on 8 Jun</b>, so its cost belongs to the JUNE calendar month,
///    not May. Two cases by requested period:
///    <list type="bullet">
///      <item><b>The cycle that ends this month</b> (i.e. the open invoice that will be finalised on
///        the next billing anchor day) → the accruing running total
///        <c>customer.subscriptions[].nextInvoiceCurrentTotal</c>. This month is determined by
///        rolling the current date forward to the next billing anchor, NOT by
///        <see cref="DateTime.UtcNow"/>.Month — after the anchor day passes the open invoice already
///        belongs to the following calendar month.</item>
///      <item><b>Any other month</b> → the finalised invoice attributed to that month via
///        <c>periodStart + InvoiceMonthOffset</c> (default +1 month, since Railway names invoices by the
///        cycle they close, not the cycle they open).</item>
///    </list>
///    Optionally scoped to one workspace via <c>WorkspaceId</c>.
/// 2. <b>Fixed amount</b> — when no token is set, falls back to <c>VendorCosts:Railway:MonthlyAmount</c>.
///
/// Config keys (under <c>VendorCosts:Railway</c>): ApiToken, WorkspaceId (optional), MonthlyAmount,
/// Currency, BillingDayOfMonth (default 8), InvoiceMonthOffset (default 1).
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

    /// <summary>
    /// Day of month on which Railway closes the billing cycle and issues the invoice (default 8).
    /// Clamped to 1..28 so <see cref="DateTime"/> construction cannot throw for short months.
    /// </summary>
    private int BillingDayOfMonth
    {
        get
        {
            var raw = _configuration["VendorCosts:Railway:BillingDayOfMonth"];
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
                   && d is >= 1 and <= 28
                ? d : 8;
        }
    }

    /// <summary>
    /// Months to add to a finalised invoice's <c>periodStart</c> to get the calendar month the invoice
    /// is filed under.  Default 1 — Railway files the invoice for cycle "8 May → 8 Jun" under JUNE
    /// (the cycle-close month), not May.
    /// </summary>
    private int InvoiceMonthOffset
    {
        get
        {
            var raw = _configuration["VendorCosts:Railway:InvoiceMonthOffset"];
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) ? o : 1;
        }
    }

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

            // Calendar month of the NEXT billing anchor date. The open invoice's running total is
            // what will be charged on that anchor day, so it belongs to that month — regardless of
            // what today's calendar month is. Example: on 22 Jun with anchor=8, the open cycle
            // closes on 8 Jul → the running total is a JULY figure, never a June one.
            var nextBilling = NextBillingDate(DateTime.UtcNow, BillingDayOfMonth);
            var wantsCurrentCycleMonth = year == nextBilling.Year && month == nextBilling.Month;

            var offset  = InvoiceMonthOffset;
            var wanted  = WorkspaceId;

            decimal totalCents = 0m;
            var matched = false;

            foreach (var ws in workspaces.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(wanted)
                    && (!ws.TryGetProperty("id", out var wsId) || wsId.GetString() != wanted))
                    continue;

                if (!ws.TryGetProperty("customer", out var customer) || customer.ValueKind != JsonValueKind.Object) continue;

                if (wantsCurrentCycleMonth)
                {
                    // The requested month is the cycle-close month for the currently open invoice.
                    // Report its running total; it becomes the finalised amountDue at cycle end.
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
                    // Any other requested month → finalised invoice(s) whose (periodStart + offset)
                    // lands on that calendar month. offset defaults to 1 so a "8 May → 8 Jun" cycle
                    // (periodStart = 8 May) is attributed to JUNE — the month the invoice was cut.
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

                            var invoiceMonth = start.UtcDateTime.AddMonths(offset);
                            if (invoiceMonth.Year != year || invoiceMonth.Month != month) continue;

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
            _logger.LogDebug("Railway cost for {Year}-{Month:D2}: {Amount} {Currency} ({Mode}, nextBilling={NextBilling:d}).",
                year, month, amount, Currency, wantsCurrentCycleMonth ? "running" : "invoice", nextBilling);
            return new CostFetchResult(amount, Currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Railway GraphQL cost query failed for {Year}-{Month:D2}.", year, month);
            return null;
        }
    }

    /// <summary>
    /// Returns the next occurrence of <paramref name="anchorDay"/> at or after <paramref name="today"/>.
    /// When today is already past the current month's anchor, rolls to the same day of the following
    /// month.  <paramref name="anchorDay"/> is expected to be within 1..28 (enforced by the caller)
    /// so short-month clamping is unnecessary.
    /// </summary>
    private static DateTime NextBillingDate(DateTime today, int anchorDay)
    {
        var thisMonthAnchor = new DateTime(today.Year, today.Month, anchorDay);
        if (today.Date <= thisMonthAnchor.Date)
            return thisMonthAnchor;

        var next = today.AddMonths(1);
        return new DateTime(next.Year, next.Month, anchorDay);
    }
}
