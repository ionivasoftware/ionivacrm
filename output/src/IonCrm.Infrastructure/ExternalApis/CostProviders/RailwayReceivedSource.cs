using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis.CostProviders;

/// <summary>
/// Pulls Railway's finalised invoices from the GraphQL API as the "received" side.
/// Query: me.workspaces[].customer.invoices[] → amountDue (cents/100), periodStart (→ calendar month),
/// invoiceId, pdfURL. Void invoices are skipped. Shares config with <see cref="RailwayCostProvider"/>
/// (VendorCosts:Railway:ApiToken / WorkspaceId / Currency).
/// </summary>
public sealed class RailwayReceivedSource : IReceivedInvoiceSource
{
    private const string GraphQlEndpoint = "https://backboard.railway.com/graphql/v2";
    private const string InvoicesQuery =
        "query{me{workspaces{id customer{invoices{amountDue periodStart periodEnd status invoiceId pdfURL}}}}}";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RailwayReceivedSource> _logger;

    /// <summary>Initialises a new instance of <see cref="RailwayReceivedSource"/>.</summary>
    public RailwayReceivedSource(HttpClient httpClient, IConfiguration configuration, ILogger<RailwayReceivedSource> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderKey => "Railway";

    private string? ApiToken => _configuration["VendorCosts:Railway:ApiToken"];
    private string? WorkspaceId => _configuration["VendorCosts:Railway:WorkspaceId"];
    private string Currency
    {
        get
        {
            var c = _configuration["VendorCosts:Railway:Currency"];
            return string.IsNullOrWhiteSpace(c) ? "USD" : c;
        }
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReceivedInvoice>> GetReceivedInvoicesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ReceivedInvoice>();
        if (string.IsNullOrWhiteSpace(ApiToken)) return result;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiToken}");
            request.Content = JsonContent.Create(new { query = InvoicesQuery });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Railway invoices GraphQL {Status}: {Body}", (int)response.StatusCode, body);
                return result;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                _logger.LogWarning("Railway invoices GraphQL errors: {Errors}", errors.ToString());
                return result;
            }
            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("me", out var me)
                || !me.TryGetProperty("workspaces", out var workspaces)
                || workspaces.ValueKind != JsonValueKind.Array)
                return result;

            var wanted = WorkspaceId;
            foreach (var ws in workspaces.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(wanted)
                    && (!ws.TryGetProperty("id", out var wsId) || wsId.GetString() != wanted))
                    continue;

                if (!ws.TryGetProperty("customer", out var customer) || customer.ValueKind != JsonValueKind.Object) continue;
                if (!customer.TryGetProperty("invoices", out var invoices) || invoices.ValueKind != JsonValueKind.Array) continue;

                foreach (var inv in invoices.EnumerateArray())
                {
                    if (inv.TryGetProperty("status", out var st)
                        && string.Equals(st.GetString(), "void", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!inv.TryGetProperty("periodStart", out var ps)
                        || !DateTimeOffset.TryParse(ps.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
                        continue;
                    if (!inv.TryGetProperty("amountDue", out var due) || due.ValueKind != JsonValueKind.Number)
                        continue;

                    // Skip zero-length-period invoices (e.g. the initial signup/base charge whose
                    // periodStart == periodEnd) so the meaningful monthly usage invoice isn't overwritten by it.
                    if (inv.TryGetProperty("periodEnd", out var pe)
                        && DateTimeOffset.TryParse(pe.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)
                        && end.UtcDateTime.Date == start.UtcDateTime.Date)
                        continue;

                    var startUtc = start.UtcDateTime;
                    var amount = Math.Round(due.GetDecimal() / 100m, 2, MidpointRounding.AwayFromZero);
                    var invoiceId = inv.TryGetProperty("invoiceId", out var iid) ? iid.GetString() : null;
                    var pdfUrl = inv.TryGetProperty("pdfURL", out var pu) ? pu.GetString() : null;

                    result.Add(new ReceivedInvoice(startUtc.Year, startUtc.Month, amount, Currency, invoiceId, pdfUrl));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Railway invoices fetch failed.");
        }

        return result;
    }
}
