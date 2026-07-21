using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Client for the Liftdesk company checklist management API
/// (docs/liftdesk-saas-checklist-contract.md). Base URL + Bearer key are passed per call — they live
/// on the Liftdesk <c>Project</c> row. <c>kind</c> is "maintenance" or "fault".
/// Methods throw <see cref="HttpRequestException"/> (with the Liftdesk response body in the message
/// and <c>StatusCode</c> set) on non-2xx responses; callers map those to legible failures.
/// </summary>
public interface ILiftdeskChecklistClient
{
    /// <summary>GET /api/v1/crm/companies/{companyId}/{kind}-checklist.</summary>
    Task<LiftdeskChecklistDoc> GetChecklistAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        string kind,
        CancellationToken cancellationToken = default);

    /// <summary>PUT /api/v1/crm/companies/{companyId}/{kind}-checklist — full-document replace.</summary>
    Task<LiftdeskChecklistDoc> UpdateChecklistAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        string kind,
        LiftdeskChecklistUpdateRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /api/v1/crm/companies/{companyId}/checklists/reset — re-seeds the DEMO default template.
    /// Destructive: the company's customisation is deleted. <paramref name="kind"/> may also be "both".
    /// </summary>
    Task<LiftdeskChecklistResetResponse> ResetChecklistsAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        string kind,
        CancellationToken cancellationToken = default);
}
