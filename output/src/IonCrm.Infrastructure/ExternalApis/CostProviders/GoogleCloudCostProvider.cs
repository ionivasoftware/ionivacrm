using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.ExternalApis.CostProviders;

/// <summary>
/// Reports Google Cloud's monthly cost. Two modes, checked in order:
///
/// 1. <b>Live BigQuery billing export</b> — when <c>ProjectId</c>, <c>BillingTable</c> and
///    <c>ServiceAccountJson</c> are all configured, sums the net cost (usage cost + credits) for the
///    period's <c>invoice.month</c> from the standard Cloud Billing export table.
///    Requires the billing export to be enabled in GCP (takes ~24h to populate).
/// 2. <b>Fixed amount</b> — otherwise falls back to <c>VendorCosts:GoogleCloud:MonthlyAmount</c>.
///
/// Config keys (all under <c>VendorCosts:GoogleCloud</c>):
///   ProjectId, BillingTable ("project.dataset.gcp_billing_export_v1_XXXXXX"), ServiceAccountJson,
///   MonthlyAmount, Currency.
/// </summary>
public sealed class GoogleCloudCostProvider : ICostProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCloudCostProvider> _logger;

    /// <summary>Initialises a new instance of <see cref="GoogleCloudCostProvider"/>.</summary>
    public GoogleCloudCostProvider(IConfiguration configuration, ILogger<GoogleCloudCostProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderKey => "GoogleCloud";

    private string? ProjectId => _configuration["VendorCosts:GoogleCloud:ProjectId"];
    private string? BillingTable => _configuration["VendorCosts:GoogleCloud:BillingTable"];
    private string? ServiceAccountJson => _configuration["VendorCosts:GoogleCloud:ServiceAccountJson"];
    private string? FixedAmountRaw => _configuration["VendorCosts:GoogleCloud:MonthlyAmount"];

    private bool HasBigQuery =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(BillingTable) && !string.IsNullOrWhiteSpace(ServiceAccountJson);

    private bool HasFixedAmount =>
        decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) && amt > 0;

    /// <inheritdoc />
    public bool IsConfigured => HasBigQuery || HasFixedAmount;

    /// <inheritdoc />
    public async Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (HasBigQuery)
        {
            var live = await QueryBigQueryAsync(year, month, cancellationToken);
            if (live is not null) return live;
            // fall through to fixed on failure
        }

        if (decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fixedAmount) && fixedAmount > 0)
        {
            var currency = _configuration["VendorCosts:GoogleCloud:Currency"];
            return new CostFetchResult(fixedAmount, string.IsNullOrWhiteSpace(currency) ? "USD" : currency);
        }

        return null;
    }

    private async Task<CostFetchResult?> QueryBigQueryAsync(int year, int month, CancellationToken cancellationToken)
    {
        try
        {
            var credential = GoogleCredential.FromJson(ServiceAccountJson);
            var client = await BigQueryClient.CreateAsync(ProjectId!, credential);

            var invoiceMonth = $"{year:D4}{month:D2}"; // billing export invoice.month is a "YYYYMM" string
            var sql =
                $"SELECT " +
                $"  ROUND(SUM(CAST(cost AS NUMERIC)) + SUM(IFNULL((SELECT SUM(CAST(c.amount AS NUMERIC)) FROM UNNEST(credits) c), 0)), 2) AS net, " +
                $"  ANY_VALUE(currency) AS currency " +
                $"FROM `{BillingTable}` " +
                $"WHERE invoice.month = @month";

            var parameters = new[] { new BigQueryParameter("month", BigQueryDbType.String, invoiceMonth) };
            var results = await client.ExecuteQueryAsync(sql, parameters, cancellationToken: cancellationToken);

            decimal total = 0m;
            string currency = "USD";
            await foreach (var row in results.GetRowsAsync().WithCancellation(cancellationToken))
            {
                if (row["net"] is not null && decimal.TryParse(row["net"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var net))
                    total += net;
                if (row["currency"] is not null && !string.IsNullOrWhiteSpace(row["currency"].ToString()))
                    currency = row["currency"].ToString()!;
            }

            _logger.LogDebug("Google Cloud BigQuery cost for {Year}-{Month:D2}: {Total} {Currency}.", year, month, total, currency);
            return new CostFetchResult(total, currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Cloud BigQuery cost query failed for {Year}-{Month:D2}.", year, month);
            return null;
        }
    }
}
