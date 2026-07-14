using IonCrm.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.VendorInvoices.CostProviders;

/// <summary>Default <see cref="ICostAutoExpectService"/> — loops registered providers and calls Expect.</summary>
public sealed class CostAutoExpectService : ICostAutoExpectService
{
    private readonly IEnumerable<ICostProvider> _providers;
    private readonly IVendorInvoiceService _invoices;
    private readonly ILogger<CostAutoExpectService> _logger;

    /// <summary>Initialises a new instance of <see cref="CostAutoExpectService"/>.</summary>
    public CostAutoExpectService(
        IEnumerable<ICostProvider> providers,
        IVendorInvoiceService invoices,
        ILogger<CostAutoExpectService> logger)
    {
        _providers = providers;
        _invoices = invoices;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AutoExpectSummary>> RunAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
            return Result<AutoExpectSummary>.Failure("Ay 1–12 aralığında olmalı.");

        var items = new List<AutoExpectItem>();

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured)
            {
                items.Add(new AutoExpectItem(provider.ProviderKey, "skipped", null, null, "Yapılandırılmamış"));
                continue;
            }

            try
            {
                var cost = await provider.GetMonthlyCostAsync(year, month, cancellationToken);
                if (cost is null)
                {
                    items.Add(new AutoExpectItem(provider.ProviderKey, "failed", null, null, "Tutar alınamadı"));
                    continue;
                }

                var res = await _invoices.ExpectAsync(
                    new ExpectRequest(provider.ProviderKey, year, month, cost.Amount, cost.Currency),
                    cancellationToken);

                items.Add(res.IsSuccess
                    ? new AutoExpectItem(provider.ProviderKey, "expected", cost.Amount, cost.Currency, null)
                    : new AutoExpectItem(provider.ProviderKey, "failed", cost.Amount, cost.Currency, res.FirstError));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cost auto-expect failed for provider {Provider} {Year}-{Month:D2}.",
                    provider.ProviderKey, year, month);
                items.Add(new AutoExpectItem(provider.ProviderKey, "failed", null, null, ex.Message));
            }
        }

        var expected = items.Count(i => i.Status == "expected");
        _logger.LogInformation("Cost auto-expect {Year}-{Month:D2}: {Expected}/{Total} sağlayıcı güncellendi.",
            year, month, expected, items.Count);

        return Result<AutoExpectSummary>.Success(new AutoExpectSummary(year, month, items));
    }
}
