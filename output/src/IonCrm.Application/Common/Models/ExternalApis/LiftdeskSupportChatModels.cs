namespace IonCrm.Application.Common.Models.ExternalApis;

// ── Liftdesk (EMS) CRM support-chat-logs API ─────────────────────────────────
// Base: https://ems-api-development.up.railway.app (dev) — configurable via Liftdesk:BaseUrl.
// Auth: static "Authorization: Bearer {Liftdesk:ApiKey}" — the SAME M2M key as error-triage/tickets.
// Read-only: a passive log of the in-app support assistant (Claude) conversations, kept 10 days on
// the EMS side. Responses use the shared LiftdeskEnvelope<T> wrapping LiftdeskPage<T>.
// Contract: docs/crm-support-chat-api.md.

/// <summary>
/// One support-assistant turn (EMS SupportChatLogDto): a user's question + the assistant's answer,
/// with the tenant + user snapshot. Rebuild a full conversation by ordering same projectId+userId by
/// <see cref="CreatedAt"/> ascending. <see cref="Answer"/> is plain text (not markdown).
/// </summary>
public record LiftdeskSupportChatLog(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    Guid? UserId,
    string? UserName,
    string? UserEmail,
    string Question,
    string Answer,
    DateTime CreatedAt);
