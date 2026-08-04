using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Client for the Liftdesk company subscription-plan API (docs/crm-company-plan-api.md).
/// Base URL + Bearer key are passed per call — they live on the Liftdesk <c>Project</c> row, the same
/// credentials the customer sync and checklist surfaces use.
/// Methods throw <see cref="HttpRequestException"/> (with the Liftdesk response body in the message
/// and <c>StatusCode</c> set) on non-2xx responses; callers map those to legible failures — notably
/// 409, which means the tenant has no subscription row yet.
/// </summary>
public interface ILiftdeskPlanClient
{
    /// <summary>GET /api/v1/crm/companies/{companyId}/plan — current plan + selectable plans.</summary>
    Task<LiftdeskCompanyPlan> GetPlanAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PUT /api/v1/crm/companies/{companyId}/plan — switches the tier. Idempotent, and the response is
    /// the same payload as the GET, so the screen does not need a second round-trip.
    /// Changing the plan does NOT change the licence period or status.
    /// </summary>
    Task<LiftdeskCompanyPlan> UpdatePlanAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        LiftdeskPlanChangeRequest body,
        CancellationToken cancellationToken = default);
}
