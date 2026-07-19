using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk (EMS) error-triage API.
/// The CRM only lists triage cards and writes approve/reject decisions; error capture, AI triage
/// and the fix pipeline live on the EMS side. Auth is a static Bearer key (Liftdesk:ApiKey) that
/// never leaves the server.
/// </summary>
public interface ILiftdeskClient
{
    /// <summary>True when Liftdesk:ApiKey is configured; the source is silently skipped otherwise.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Lists error-analysis cards.
    /// Endpoint: GET /api/v1/crm/error-analyses?status={status}&amp;page={page}&amp;pageSize={pageSize}
    /// <paramref name="status"/> null/empty returns all statuses.
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskPage<LiftdeskErrorAnalysis>>> GetErrorAnalysesAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects a Triaged card.
    /// Endpoint: PATCH /api/v1/crm/error-analyses/{id}/status
    /// Body: { status: "Approved", approvedBy } or { status: "Rejected", rejectReason }.
    /// EMS returns 409 for invalid transitions (e.g. already Approved) — surfaced via the envelope.
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskErrorAnalysis>> UpdateErrorAnalysisStatusAsync(
        Guid id,
        string status,
        string? approvedBy,
        string? rejectReason,
        CancellationToken cancellationToken = default);
}
