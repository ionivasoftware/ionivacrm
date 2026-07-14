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
/// 1. <b>Live GraphQL</b> — when <c>ApiToken</c>, <c>GraphQlQuery</c> and <c>AmountJsonPath</c> are all
///    configured, POSTs the query to https://backboard.railway.com/graphql/v2 (Authorization: Bearer token)
///    and reads the numeric cost at the given JSON path. Railway's cost/usage query is not publicly
///    documented and varies by plan, so the query + result path are supplied via configuration rather
///    than hard-coded — set them from your account's GraphiQL playground (railway.com/graphiql).
///    The query may contain the tokens {year}, {month}, {monthStart}, {monthEnd} which are substituted
///    (RFC3339 UTC for the *Start/*End tokens).
/// 2. <b>Fixed amount</b> — otherwise falls back to <c>VendorCosts:Railway:MonthlyAmount</c>.
///
/// Config keys (all under <c>VendorCosts:Railway</c>):
///   ApiToken, GraphQlQuery, AmountJsonPath ("data.estimatedUsage.0.cost"), Currency, MonthlyAmount.
/// </summary>
public sealed class RailwayCostProvider : ICostProvider
{
    private const string GraphQlEndpoint = "https://backboard.railway.com/graphql/v2";

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
    private string? GraphQlQuery => _configuration["VendorCosts:Railway:GraphQlQuery"];
    private string? AmountJsonPath => _configuration["VendorCosts:Railway:AmountJsonPath"];
    private string? FixedAmountRaw => _configuration["VendorCosts:Railway:MonthlyAmount"];

    private bool HasGraphQl =>
        !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(GraphQlQuery) && !string.IsNullOrWhiteSpace(AmountJsonPath);

    private bool HasFixedAmount =>
        decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) && amt > 0;

    /// <inheritdoc />
    public bool IsConfigured => HasGraphQl || HasFixedAmount;

    /// <inheritdoc />
    public async Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (HasGraphQl)
        {
            var live = await QueryGraphQlAsync(year, month, cancellationToken);
            if (live is not null) return live;
            // fall through to fixed on failure
        }

        if (decimal.TryParse(FixedAmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fixedAmount) && fixedAmount > 0)
        {
            var currency = _configuration["VendorCosts:Railway:Currency"];
            return new CostFetchResult(fixedAmount, string.IsNullOrWhiteSpace(currency) ? "USD" : currency);
        }

        return null;
    }

    private async Task<CostFetchResult?> QueryGraphQlAsync(int year, int month, CancellationToken cancellationToken)
    {
        try
        {
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            var query = GraphQlQuery!
                .Replace("{year}", year.ToString(CultureInfo.InvariantCulture))
                .Replace("{month}", month.ToString(CultureInfo.InvariantCulture))
                .Replace("{monthStart}", start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                .Replace("{monthEnd}", end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiToken}");
            request.Content = JsonContent.Create(new { query });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Railway GraphQL {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!TryNavigate(doc.RootElement, AmountJsonPath!, out var element))
            {
                _logger.LogWarning("Railway GraphQL: amount path '{Path}' not found. Body: {Body}", AmountJsonPath, body);
                return null;
            }

            if (!TryReadDecimal(element, out var amount))
            {
                _logger.LogWarning("Railway GraphQL: value at '{Path}' is not numeric.", AmountJsonPath);
                return null;
            }

            var currency = _configuration["VendorCosts:Railway:Currency"];
            return new CostFetchResult(amount, string.IsNullOrWhiteSpace(currency) ? "USD" : currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Railway GraphQL cost query failed for {Year}-{Month:D2}.", year, month);
            return null;
        }
    }

    /// <summary>Navigates a dot-separated path (object keys + numeric array indices) into a JsonElement.</summary>
    private static bool TryNavigate(JsonElement root, string path, out JsonElement result)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                if (current.ValueKind != JsonValueKind.Array || index < 0 || index >= current.GetArrayLength())
                {
                    result = default;
                    return false;
                }
                current = current[index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                {
                    result = default;
                    return false;
                }
                current = next;
            }
        }
        result = current;
        return true;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out value):
                return true;
            case JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value):
                return true;
            default:
                value = 0m;
                return false;
        }
    }
}
