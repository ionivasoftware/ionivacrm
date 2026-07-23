namespace IonCrm.Application.Common.Models.ExternalApis;

// ── Liftdesk (EMS) CRM ticket API ────────────────────────────────────────────
// Base: https://ems-api-development.up.railway.app (dev) — configurable via Liftdesk:BaseUrl.
// Auth: static "Authorization: Bearer {Liftdesk:ApiKey}" — the SAME M2M key as error-triage.
// Responses use the shared LiftdeskEnvelope<T>; list wraps LiftdeskPage<T>. camelCase, UTC dates.
// Contract: docs/crm-ticket-api.md.

/// <summary>
/// A support ticket from the CRM view (EMS CrmTicketDto — all fields incl. internal agent columns).
/// Status machine: New → Triaged (analysis) → Approved|Rejected (CRM) → InProgress → Done|Failed (agent).
/// </summary>
public record LiftdeskTicket(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    Guid? CreatedByUserId,
    string CreatedByName,
    string Source,          // Tenant | Crm
    string Type,            // Feedback | Suggestion
    string Platform,        // Web | MobileStaff | CustomerPortal | CustomerMobile
    string Subject,
    string Description,
    string Status,          // New | Triaged | Approved | Rejected | InProgress | Done | Failed
    string? AgentComment,
    string? AgentSuggestedAction,
    DateTime? AgentAnalyzedAt,
    string? DecisionNote,
    string? DecidedBy,
    DateTime? DecidedAt,
    string? ResolutionNote,
    string? FixBranch,
    string? FixPrUrl,
    string? FailReason,
    DateTime? CompletedAt,
    DateTime CreatedAt);
