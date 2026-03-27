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
    /// Fetches a page of customers from the EMS CRM endpoint with optional delta sync.
    /// GET /api/v1/crm/customers?page={page}&amp;pageSize={pageSize}[&amp;updatedSince={updatedSince:O}]
    /// </summary>
    Task<EmsCrmCustomersResponse> GetCrmCustomersPageAsync(
        string? apiKey,
        int page,
        int pageSize,
        DateTime? updatedSince = null,
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
}
