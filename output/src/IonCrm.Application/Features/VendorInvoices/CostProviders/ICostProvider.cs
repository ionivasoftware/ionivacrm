namespace IonCrm.Application.Features.VendorInvoices.CostProviders;

/// <summary>The monthly cost a provider reports for a period.</summary>
public record CostFetchResult(decimal Amount, string Currency);

/// <summary>
/// Fetches the expected monthly cost for one vendor (Phase 2 auto-expect).
/// Each provider is gated on its own configuration; unconfigured providers are skipped, not errored.
/// Add a new integration by implementing this and registering it in DI — the orchestrator picks it up.
/// </summary>
public interface ICostProvider
{
    /// <summary>Provider key, matching a <see cref="KnownProviders"/> entry (e.g. "Anthropic", "Railway").</summary>
    string ProviderKey { get; }

    /// <summary>True when the credentials/values this provider needs are present in configuration.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Returns the provider's total cost for the given period, or null when unavailable
    /// (not configured, remote error, or no data). Implementations must not throw for expected failures.
    /// </summary>
    Task<CostFetchResult?> GetMonthlyCostAsync(int year, int month, CancellationToken cancellationToken = default);
}
