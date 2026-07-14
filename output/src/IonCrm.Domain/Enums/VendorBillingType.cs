namespace IonCrm.Domain.Enums;

/// <summary>
/// How a vendor bills for a given period.
/// <see cref="Usage"/> — amount varies per period, known from a cost API (Anthropic, Railway, Google Cloud).
/// <see cref="Fixed"/> — flat recurring amount, no cost API (Google Workspace seat subscription).
/// </summary>
public enum VendorBillingType
{
    Usage = 1,
    Fixed = 2
}
