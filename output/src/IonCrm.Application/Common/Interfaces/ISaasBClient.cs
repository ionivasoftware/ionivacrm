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
    /// <summary>Fetches all customers from SaaS B.</summary>
    Task<SaasBCustomersResponse> GetCustomersAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches all subscriptions from SaaS B.</summary>
    Task<SaasBSubscriptionsResponse> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches all orders from SaaS B.</summary>
    Task<SaasBOrdersResponse> GetOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an outbound CRM event to SaaS B's webhook endpoint.
    /// Called instantly when: subscription extended, status changed, etc.
    /// </summary>
    Task NotifyCallbackAsync(SaasBCallbackPayload payload, CancellationToken cancellationToken = default);
}
