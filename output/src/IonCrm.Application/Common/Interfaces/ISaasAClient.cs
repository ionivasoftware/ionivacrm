using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// HTTP client contract for SaaS A external system.
/// SaaS A uses a standard REST API under /api/v1/ with Bearer token auth.
/// Implemented by Infrastructure.ExternalApis.SaasAClient.
/// </summary>
public interface ISaasAClient
{
    /// <summary>Fetches all customers from SaaS A (EMS) legacy endpoint.</summary>
    /// <param name="apiKey">Project-specific EMS API key. Overrides the default configured key when provided.</param>
    Task<SaasACustomersResponse> GetCustomersAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page of customers from the EMS CRM endpoint (full sync — no delta filter).
    /// GET /api/v1/crm/customers?page={page}&amp;pageSize={pageSize}
    /// </summary>
    Task<EmsCrmCustomersResponse> GetCrmCustomersPageAsync(
        string? apiKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches all subscriptions from SaaS A (EMS).</summary>
    Task<SaasASubscriptionsResponse> GetSubscriptionsAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches all orders from SaaS A (EMS).</summary>
    Task<SaasAOrdersResponse> GetOrdersAsync(string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an outbound CRM event to SaaS A's callback endpoint.
    /// Called instantly when: subscription extended, status changed, etc.
    /// </summary>
    Task NotifyCallbackAsync(SaasACallbackPayload payload, string? apiKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends a company's expiration date via the EMS CRM API.
    /// POST /api/v1/crm/companies/{emsCompanyId}/extend-expiration
    /// </summary>
    Task<EmsExtendExpirationResponse> ExtendExpirationAsync(
        string? apiKey,
        int emsCompanyId,
        string durationType,
        int amount,
        CancellationToken cancellationToken = default,
        string? baseUrl = null);

    /// <summary>
    /// Adds SMS credits to a company via the EMS CRM API.
    /// POST /api/v1/crm/companies/{emsCompanyId}/add-sms
    /// </summary>
    Task<EmsAddSmsResponse> AddSmsAsync(
        string? apiKey,
        int emsCompanyId,
        int count,
        CancellationToken cancellationToken = default,
        string? baseUrl = null);

    /// <summary>
    /// Fetches the user list for a company via the EMS CRM API.
    /// GET /api/v1/crm/companies/{companyId}/users
    /// </summary>
    Task<List<EmsCompanyUser>> GetCompanyUsersAsync(
        string? apiKey,
        int companyId,
        CancellationToken cancellationToken = default,
        string? baseUrl = null);

    /// <summary>
    /// Fetches the usage summary for a company via the EMS CRM API.
    /// GET /api/v1/crm/companies/{companyId}/summary
    /// Returns monthly maintenance/breakdown/proposal counts and overall totals.
    /// </summary>
    Task<EmsCompanySummaryResponse> GetCompanySummaryAsync(
        string? apiKey,
        int companyId,
        CancellationToken cancellationToken = default,
        string? baseUrl = null);

    /// <summary>
    /// Fetches recent completed payments from the EMS CRM API.
    /// GET /api/v1/crm/payments/recent
    /// Returns payments with CompletionPayment=1 created within the last <paramref name="windowMinutes"/> minutes,
    /// ordered by CreatedOn DESC.
    /// </summary>
    Task<EmsRecentPaymentsResponse> GetRecentPaymentsAsync(
        string? apiKey,
        int windowMinutes = 20,
        CancellationToken cancellationToken = default,
        string? baseUrl = null);
}
