using IonCrm.Application.Common.Models;

namespace IonCrm.Application.Features.VendorInvoices.CostProviders;

/// <summary>Outcome of auto-expect for a single provider.</summary>
/// <param name="Provider">Provider key.</param>
/// <param name="Status">"expected" (fed into Expect), "skipped" (not configured), or "failed".</param>
/// <param name="Amount">Fetched amount, when available.</param>
/// <param name="Currency">Fetched currency, when available.</param>
/// <param name="Message">Reason for skip/failure, when applicable.</param>
public record AutoExpectItem(string Provider, string Status, decimal? Amount, string? Currency, string? Message);

/// <summary>Summary of an auto-expect run for a period.</summary>
public record AutoExpectSummary(int Year, int Month, IReadOnlyList<AutoExpectItem> Items);

/// <summary>
/// Phase 2 orchestrator: pulls each configured provider's monthly cost and feeds it into
/// <see cref="IVendorInvoiceService.ExpectAsync"/> (idempotent). Runs monthly via the background
/// service and on demand via the API.
/// </summary>
public interface ICostAutoExpectService
{
    /// <summary>Fetches costs from all configured providers for the period and upserts Expected rows.</summary>
    Task<Result<AutoExpectSummary>> RunAsync(int year, int month, CancellationToken cancellationToken = default);
}
