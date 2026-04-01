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
}
