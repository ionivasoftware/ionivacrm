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
    /// <summary>
    /// GET /api/v1/crm/companies/{companyId}/{kind}-checklist[?type=].
    /// <paramref name="type"/> selects the equipment family of a MAINTENANCE list
    /// (1 = elevator, 2 = escalator); it is ignored for the fault list.
    /// <paramref name="culture"/> selects the language (null = the company's own).
    /// </summary>
    Task<LiftdeskChecklistDoc> GetChecklistAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        string kind,
        int? type,
        int? culture,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PUT /api/v1/crm/companies/{companyId}/{kind}-checklist[?type=] — full-document replace.
    /// The replace is SCOPED to <paramref name="type"/> AND <paramref name="culture"/>: the other
    /// equipment family and the other languages are left intact, so the type+culture used to read a
    /// list must be the same ones used to save it.
    /// </summary>
    Task<LiftdeskChecklistDoc> UpdateChecklistAsync(
        string baseUrl,
        string apiKey,
        int companyId,
        string kind,
        int? type,
        int? culture,
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
        int? culture,
        CancellationToken cancellationToken = default);
}
