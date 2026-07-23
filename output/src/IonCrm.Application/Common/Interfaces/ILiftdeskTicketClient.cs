using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk (EMS) CRM ticket API (docs/crm-ticket-api.md). The CRM lists tickets,
/// opens support tickets and writes approve/reject decisions; AI triage and the fix pipeline live on
/// the EMS side. Auth is the static Bearer key <c>Liftdesk:ApiKey</c> (shared with error-triage) that
/// never leaves the server. Non-2xx responses that still carry the EMS envelope (400/401/404/409/503)
/// are returned as failed envelopes so the controller can surface the EMS message verbatim.
/// </summary>
public interface ILiftdeskTicketClient
{
    /// <summary>True when Liftdesk:ApiKey is configured; the ticket screen 400s cleanly otherwise.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Lists tickets (cross-tenant), newest first. All filters are optional (null/empty → not applied).
    /// Endpoint: GET /api/v1/crm/tickets?status=&amp;type=&amp;platform=&amp;projectId=&amp;page=&amp;pageSize=
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskPage<LiftdeskTicket>>> GetTicketsAsync(
        string? status,
        string? type,
        string? platform,
        Guid? projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single ticket's full detail. Endpoint: GET /api/v1/crm/tickets/{id}. 404 → failed envelope.</summary>
    Task<LiftdeskEnvelope<LiftdeskTicket>> GetTicketAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a support ticket (Source=Crm, Status=New). <paramref name="projectId"/> null → global ticket.
    /// Endpoint: POST /api/v1/crm/tickets. Invalid type/platform → 400 (failed envelope).
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskTicket>> CreateTicketAsync(
        Guid? projectId,
        string type,
        string platform,
        string subject,
        string description,
        string createdByName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects a ticket. Endpoint: PATCH /api/v1/crm/tickets/{id}/status.
    /// Body: { status, decidedBy, decisionNote }. EMS returns 409 for invalid transitions — surfaced
    /// via the envelope. <paramref name="decidedBy"/> is derived from the authenticated SuperAdmin.
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskTicket>> UpdateTicketStatusAsync(
        Guid id,
        string status,
        string? decidedBy,
        string? decisionNote,
        CancellationToken cancellationToken = default);
}
