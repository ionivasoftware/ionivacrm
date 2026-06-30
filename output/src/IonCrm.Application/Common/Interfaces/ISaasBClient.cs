using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// HTTP client contract for SaaS B external system.
/// SaaS B uses a different endpoint structure (/customers/list, /subscriptions/all)
/// with X-Api-Key header authentication.
/// Implemented by Infrastructure.ExternalApis.SaasBClient.
/// </summary>
public interface ISaasBClient
{
    /// <summary>Fetches all customers from SaaS B (Rezerval).</summary>
    /// <param name="apiKey">Project-specific Rezerval API key. Overrides the default configured key when provided.</param>
    Task<SaasBCustomersResponse> GetCustomersAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches all subscriptions from SaaS B (Rezerval).</summary>
    Task<SaasBSubscriptionsResponse> GetSubscriptionsAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches all orders from SaaS B (Rezerval).</summary>
    Task<SaasBOrdersResponse> GetOrdersAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an outbound CRM event to SaaS B's webhook endpoint.
    /// Called instantly when: subscription extended, status changed, etc.
    /// </summary>
    Task NotifyCallbackAsync(SaasBCallbackPayload payload, string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all companies from the Rezerval CRM API.
    /// Endpoint: GET https://rezback.rezerval.com/v1/Crm/CompanyList
    /// Auth: Authorization: Bearer {apiKey}
    /// </summary>
    Task<List<RezervalCompany>> GetRezervalCompaniesAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new company in the RezervAl CRM system.
    /// Endpoint: POST https://rezback.rezerval.com/v1/Crm/Company
    /// Auth: Authorization: Bearer {apiKey}
    /// Content-Type: multipart/form-data
    /// </summary>
    Task<RezervalCreateCompanyResponse> CreateRezervalCompanyAsync(
        RezervalCompanyFormData data,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing company in the RezervAl CRM system.
    /// Endpoint: PUT https://rezback.rezerval.com/v1/Crm/Company/{companyId}
    /// Auth: Authorization: Bearer {apiKey}
    /// Content-Type: multipart/form-data
    /// </summary>
    Task UpdateRezervalCompanyAsync(
        int companyId,
        RezervalCompanyFormData data,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an iyzico subscription + payment plan for a RezervAl customer.
    /// RezervAl handles the iyzico API call internally and returns the resulting subscription
    /// and payment plan references which the CRM stores on the local <c>CustomerContract</c>.
    /// Endpoint: POST https://rezback.rezerval.com/v1/Crm/Subscription
    /// Auth: Authorization: Bearer {jwt}
    /// Content-Type: application/json
    /// </summary>
    Task<RezervalSubscriptionResponse> CreateRezervalSubscriptionAsync(
        RezervalSubscriptionRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades (renews) an existing iyzico subscription without re-binding the customer's card.
    /// Rezerval is expected to translate this into iyzico's "Upgrade Subscription" call, which
    /// swaps the subscription's pricing plan reference while preserving the saved card so the
    /// customer is not redirected to a new checkout flow. Use this instead of cancel + create
    /// whenever an active Rezerval subscription already exists for the customer.
    /// Endpoint: POST https://rezback.rezerval.com/v1/Crm/Subscription/Upgrade
    /// Auth: Authorization: Bearer {jwt}
    /// Content-Type: application/json
    /// Returns the same <see cref="RezervalSubscriptionResponse"/> shape as Create.
    /// </summary>
    Task<RezervalSubscriptionResponse> UpgradeRezervalSubscriptionAsync(
        RezervalUpgradeSubscriptionRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the active iyzico subscription for a RezervAl customer. Tolerant on the Rezerval
    /// side: iyzico-side failures (already deleted, network timeout) are returned as warnings in
    /// <see cref="RezervalCancelSubscriptionData.IyzicoWarnings"/> rather than throwing.
    /// Endpoint: POST https://rezback.rezerval.com/v1/Crm/Subscription/Cancel
    /// Auth: Authorization: Bearer {jwt}
    /// Content-Type: application/json
    /// </summary>
    Task<RezervalCancelSubscriptionResponse> CancelRezervalSubscriptionAsync(
        RezervalCancelSubscriptionRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregated reservation/SMS metrics for a Rezerval company over the last week,
    /// last month, and last 3 months.
    /// Endpoint: GET https://rezback.rezerval.com/v1/Crm/CompanySummary?companyId={id}
    /// Auth: Authorization: Bearer {jwt}
    /// </summary>
    Task<RezervalCompanySummaryResponse> GetCompanySummaryAsync(
        int companyId,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the reservation-side configuration (SMS texts, confirm/review cadence, flags) for a Rezerval company.
    /// Endpoint: GET https://rezback.rezerval.com/v1/Crm/ReservationSetting?companyId={id}
    /// </summary>
    Task<RezervalReservationSettingResponse> GetReservationSettingAsync(
        int companyId,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the reservation-side configuration for a Rezerval company. Only the fields present
    /// in <paramref name="request"/> are updated; omitted/null fields keep their existing values.
    /// Endpoint: PUT https://rezback.rezerval.com/v1/Crm/ReservationSetting
    /// </summary>
    Task<RezervalSimpleResponse> UpdateReservationSettingAsync(
        RezervalReservationSettingUpdateRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists triaged error cards from the RezervAl error-triage queue.
    /// Endpoint: GET https://rezback.rezerval.com/v1/ErrorTriage?status={status}&amp;page={page}&amp;pageSize={pageSize}
    /// Auth: Authorization: Bearer {jwt}
    /// Returns the full envelope so callers can surface <c>errorResponse.message</c> on failure.
    /// </summary>
    Task<RezervalErrorTriageListResponse> GetErrorTriageAsync(
        string status,
        int page,
        int pageSize,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an approve/reject decision for a triaged error card back to RezervAl.
    /// Endpoint: PATCH https://rezback.rezerval.com/v1/ErrorTriage/{triageId}/status
    /// Body: { "status": "Approved" | "Rejected", "approvedBy": "{user}" }
    /// Auth: Authorization: Bearer {jwt}
    /// Returns the updated card envelope; <c>IsSuccess=false</c> carries the RezervAl error (invalid transition, not found, …).
    /// </summary>
    Task<RezervalErrorTriageCardResponse> UpdateErrorTriageStatusAsync(
        int triageId,
        string status,
        string approvedBy,
        string? apiKey = null,
        CancellationToken cancellationToken = default);
}
