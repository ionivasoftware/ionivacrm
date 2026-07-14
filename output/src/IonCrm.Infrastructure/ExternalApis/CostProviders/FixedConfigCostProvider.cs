using System.Globalization;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using Microsoft.Extensions.Configuration;

namespace IonCrm.Infrastructure.ExternalApis.CostProviders;

/// <summary>
/// A cost provider whose monthly amount is a fixed configured value rather than a live API call.
/// Reads <c>VendorCosts:{ProviderKey}:MonthlyAmount</c> (decimal) and <c>:Currency</c> (default USD).
///
/// Used for Google Workspace (genuinely a flat subscription with no cost API) and, until their live
/// integrations land, for Railway (GraphQL billing) and Google Cloud (BigQuery billing export) —
/// set a fixed figure to auto-expect the same amount each month. Swap in a live <see cref="ICostProvider"/>
/// for those keys when ready; nothing else in the flow changes.
/// </summary>
public sealed class FixedConfigCostProvider : ICostProvider
{
    private readonly IConfiguration _configuration;

    /// <summary>Initialises the provider for a specific provider key.</summary>
    public FixedConfigCostProvider(string providerKey, IConfiguration configuration)
    {
        ProviderKey = providerKey;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string ProviderKey { get; }

    private string? AmountRaw => _configuration[$"VendorCosts:{ProviderKey}:MonthlyAmount"];

    /// <inheritdoc />
    public bool IsConfigured =>
        decimal.TryParse(AmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) && amount > 0;

    /// <inheritdoc />
    public Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (!decimal.TryParse(AmountRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return Task.FromResult<CostFetchResult?>(null);

        var currency = _configuration[$"VendorCosts:{ProviderKey}:Currency"];
        currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
        return Task.FromResult<CostFetchResult?>(new CostFetchResult(amount, currency));
    }
}
