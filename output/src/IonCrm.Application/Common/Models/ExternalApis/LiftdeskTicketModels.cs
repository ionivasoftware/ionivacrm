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
    /// <summary>
    /// CRM-only instruction telling the fix agent HOW to implement the request. Written by the
    /// SuperAdmin at approve time; never shown to the tenant (unlike <c>DecisionNote</c>). When empty
    /// the fix agent falls back to <c>AgentSuggestedAction</c> (the triage agent's proposal).
    /// Null until the Liftdesk side ships the field (see docs/liftdesk-ticket-fix-instruction-spec.md).
    /// </summary>
    string? FixInstruction,
    string? ResolutionNote,
    string? FixBranch,
    string? FixPrUrl,
    string? FailReason,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    /// <summary>
    /// When the fix agent last attempted this ticket (UTC), stamped on its InProgress/Done/Failed
    /// transitions. Unlike <c>CompletedAt</c> a re-approve does NOT clear it, so the card can still
    /// show "last tried at" after the ticket goes back to Approved. Superadmin decisions (approve /
    /// reject / manual close) never stamp it — they are not agent attempts.
    /// Null until the Liftdesk side ships the field.
    /// </summary>
    DateTime? FixAttemptedAt = null);
