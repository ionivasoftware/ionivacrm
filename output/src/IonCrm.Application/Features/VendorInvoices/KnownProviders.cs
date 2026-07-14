using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.VendorInvoices;

/// <summary>
/// The vendors seeded each month by <c>SeedMonth</c>. Add a new entry here to start tracking
/// another provider — nothing else in the reconciliation flow needs to change.
/// </summary>
public static class KnownProviders
{
    /// <summary>A provider baseline: its key, how it bills, and the default due day.</summary>
    public record Provider(string Key, VendorBillingType BillingType, int DueDay = 7);

    /// <summary>
    /// Baseline provider list used by SeedMonth. Google Workspace is split per billing account
    /// (rezerval.com and ioniva.com) so each account is tracked as its own line item.
    /// </summary>
    public static readonly IReadOnlyList<Provider> All = new List<Provider>
    {
        new("Anthropic",               VendorBillingType.Usage, 7),
        new("Railway",                 VendorBillingType.Usage, 7),
        new("GoogleCloud",             VendorBillingType.Usage, 7),
        new("GoogleWorkspaceRezerval", VendorBillingType.Fixed, 7),
        new("GoogleWorkspaceIoniva",   VendorBillingType.Fixed, 7),
    };
}
