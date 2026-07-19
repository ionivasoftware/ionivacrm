namespace IonCrm.Application.Common.Models.ExternalApis;

// ── Liftdesk (EMS) CRM error-triage API ──────────────────────────────────────
// Base: https://ems-api-development.up.railway.app (dev) — configurable via Liftdesk:BaseUrl.
// Auth: static "Authorization: Bearer {Liftdesk:ApiKey}" (M2M shared key, EMS Railway CRM__APIKEY).
// All responses use the common envelope; fields are camelCase, dates UTC ISO-8601.

/// <summary>Common EMS response envelope: { success, data, message, errors, statusCode }.</summary>
public record LiftdeskEnvelope<T>(
    bool Success,
    T? Data,
    string? Message,
    List<string>? Errors,
    int StatusCode);

/// <summary>Paginated payload inside <see cref="LiftdeskEnvelope{T}"/> for list endpoints.</summary>
public record LiftdeskPage<T>(
    List<T>? Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

/// <summary>
/// A triage card from EMS GET /api/v1/crm/error-analyses (ErrorAnalysisDto).
/// Status machine: Triaged → Approved|Rejected (CRM) → Fixing → Fixed|Failed (fix agent).
/// </summary>
public record LiftdeskErrorAnalysis(
    Guid Id,
    Guid ClientErrorId,
    string? Explanation,
    string? RootCause,
    string? SuggestedFix,
    string? SourceFile,
    string? Severity,       // Low | Medium | High | Critical
    string? Status,         // Triaged | Approved | Rejected | Fixing | Fixed | Failed
    string? ApprovedBy,
    DateTime? ApprovedOn,
    DateTime? ReviewedAt,
    string? RejectReason,
    string? FixBranch,
    string? FixCommitSha,
    string? FixPrUrl,
    string? FailReason,
    DateTime? FixedAt,
    int OccurrenceCount,
    DateTime? CreatedAt,
    LiftdeskClientError? ClientError);

/// <summary>Embedded raw-error summary (ClientErrorDto) inside <see cref="LiftdeskErrorAnalysis"/>.</summary>
public record LiftdeskClientError(
    Guid Id,
    string? Source,         // Backend | Frontend | CustomerPortal | MobileStaff | CustomerMobile | Website
    string? ErrorType,      // e.g. NullReferenceException, TypeError
    string? Message,
    string? Details,        // stack/details (PII-scrubbed)
    string? ContextJson,
    int OccurrenceCount,
    DateTime? FirstSeenAt,
    DateTime? LastSeenAt,
    string? Status);        // New | Analyzed | Ignored
