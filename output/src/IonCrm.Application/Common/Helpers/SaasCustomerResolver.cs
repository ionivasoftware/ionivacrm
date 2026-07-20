using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Helpers;

/// <summary>Which external SaaS a synced customer originates from.</summary>
public enum SaasSourceKind
{
    /// <summary>Not a SaaS-managed customer (manual PotentialCustomer, RezervAl, or unparseable id).</summary>
    None,
    /// <summary>EMS (SaaS A) — LegacyId is plain numeric or the legacy "SAASA-{id}" form.</summary>
    Ems,
    /// <summary>Liftdesk — LegacyId is the "LIFT-{id}" form. Same API surface as EMS.</summary>
    Liftdesk
}

/// <summary>
/// Resolves the numeric company id and the correct per-project credentials for a customer that was
/// synced from an EMS-style SaaS (EMS or Liftdesk). Centralises the LegacyId parsing that the
/// extend-expiration, add-sms, users and summary handlers all share so the two sources stay in step.
///
/// LegacyId conventions:
///   "3"        → EMS canonical (plain numeric)
///   "SAASA-3"  → EMS legacy prefix (older sync runs)
///   "LIFT-3"   → Liftdesk
///   "PC-…"     → manual PotentialCustomer (not a SaaS company)
///   "REZV-…" / "SAASB-…" → RezervAl (handled elsewhere)
/// </summary>
public static class SaasCustomerResolver
{
    /// <summary>
    /// Attempts to resolve <paramref name="customer"/> to its SaaS company id and the credentials to
    /// call that SaaS with. Credentials come from the customer's own <paramref name="project"/> row
    /// (null values let the client fall back to its DI-configured defaults).
    /// </summary>
    /// <returns><c>true</c> when the customer is an EMS/Liftdesk company with a numeric id; otherwise <c>false</c>.</returns>
    public static bool TryResolve(
        Customer customer,
        Project? project,
        out int companyId,
        out string? apiKey,
        out string? baseUrl,
        out SaasSourceKind kind)
    {
        companyId = 0;
        apiKey = null;
        baseUrl = null;
        kind = SaasSourceKind.None;

        var legacyId = customer.LegacyId;
        if (string.IsNullOrEmpty(legacyId)
            || legacyId.StartsWith("PC-", StringComparison.OrdinalIgnoreCase)
            || legacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)
            || legacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (legacyId.StartsWith("LIFT-", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(legacyId["LIFT-".Length..], out companyId))
                return false;

            apiKey = project?.LiftdeskApiKey;
            baseUrl = project?.LiftdeskBaseUrl;
            kind = SaasSourceKind.Liftdesk;
            return true;
        }

        var rawId = legacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)
            ? legacyId["SAASA-".Length..]
            : legacyId;

        if (!int.TryParse(rawId, out companyId))
            return false;

        apiKey = project?.EmsApiKey;
        baseUrl = project?.EmsBaseUrl;
        kind = SaasSourceKind.Ems;
        return true;
    }
}
