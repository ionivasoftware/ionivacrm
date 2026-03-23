using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// HTTP client contract for SaaS A external system.
/// SaaS A uses a standard REST API under /api/v1/ with Bearer token auth.
/// Implemented by Infrastructure.ExternalApis.SaasAClient.
/// </summary>
public interface ISaasAClient
{
    /// <summary>Fetches all customers from SaaS A.</summary>
    Task<SaasACustomersResponse> GetCustomersAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches all subscriptions from SaaS A.</summary>
    Task<SaasASubscriptionsResponse> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches all orders from SaaS A.</summary>
    Task<SaasAOrdersResponse> GetOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an outbound CRM event to SaaS A's callback endpoint.
    /// Called instantly when: subscription extended, status changed, etc.
    /// </summary>
    Task NotifyCallbackAsync(SaasACallbackPayload payload, CancellationToken cancellationToken = default);
}
