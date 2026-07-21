using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk (EMS) pricing management API
/// (<c>{baseUrl}/api/v1/crm/pricing/...</c>, static Bearer key). Credentials are supplied per call
/// because they live on the Liftdesk <c>Project</c> row rather than in config. All methods return the
/// shared <see cref="LiftdeskEnvelope{T}"/>; non-2xx responses carry a legible <c>Message</c>.
/// </summary>
public interface ILiftdeskPricingClient
{
    /// <summary>GET /plans — all subscription plans (including inactive).</summary>
    Task<LiftdeskEnvelope<List<LiftdeskPricingPlan>>> GetPlansAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>PUT /plans/{id} — full-replace update of an existing plan.</summary>
    Task<LiftdeskEnvelope<LiftdeskPricingPlan>> UpdatePlanAsync(
        string baseUrl, string apiKey, Guid id, UpdatePricingPlanRequest body, CancellationToken cancellationToken = default);

    /// <summary>GET /sms-packages — all SMS packages (including inactive).</summary>
    Task<LiftdeskEnvelope<List<LiftdeskSmsPackage>>> GetSmsPackagesAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>POST /sms-packages — create a new SMS package.</summary>
    Task<LiftdeskEnvelope<LiftdeskSmsPackage>> CreateSmsPackageAsync(
        string baseUrl, string apiKey, CreateSmsPackageRequest body, CancellationToken cancellationToken = default);

    /// <summary>PUT /sms-packages/{id} — update an SMS package.</summary>
    Task<LiftdeskEnvelope<LiftdeskSmsPackage>> UpdateSmsPackageAsync(
        string baseUrl, string apiKey, Guid id, UpdateSmsPackageRequest body, CancellationToken cancellationToken = default);

    /// <summary>DELETE /sms-packages/{id} — soft-delete (deactivate) an SMS package.</summary>
    Task<LiftdeskEnvelope<object>> DeleteSmsPackageAsync(
        string baseUrl, string apiKey, Guid id, CancellationToken cancellationToken = default);
}
