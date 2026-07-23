using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// M2M client for the Liftdesk (EMS) CRM support-chat-logs API (docs/crm-support-chat-api.md).
/// Read-only: the CRM lists the support-assistant conversation logs (question/answer pairs) so the
/// support team can spot gaps in the help docs. Auth is the static Bearer key <c>Liftdesk:ApiKey</c>
/// (shared with error-triage/tickets) that never leaves the server. Non-2xx responses that still
/// carry the EMS envelope are returned as failed envelopes so the controller can surface the message.
/// </summary>
public interface ILiftdeskSupportChatClient
{
    /// <summary>True when Liftdesk:ApiKey is configured; the screen 400s cleanly otherwise.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Lists support-chat logs (cross-tenant), newest first. All filters optional (null/empty → not applied).
    /// <paramref name="startDate"/>/<paramref name="endDate"/> are passed through verbatim as ISO 8601
    /// strings (the EMS side parses them and 400s on a bad format).
    /// Endpoint: GET /api/v1/crm/support-chat-logs?projectId=&amp;search=&amp;startDate=&amp;endDate=&amp;page=&amp;pageSize=
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskPage<LiftdeskSupportChatLog>>> GetLogsAsync(
        Guid? projectId,
        string? search,
        string? startDate,
        string? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
