namespace IonCrm.Application.Common.Models.ExternalApis;

// Models for the Liftdesk company checklist management API (docs/liftdesk-saas-checklist-contract.md).
// Same api/v1/crm surface + Bearer key as the SaaS integration, but responses are FLAT — no
// {success,data,message} envelope. Field names are camelCase on the wire.

/// <summary>A single checklist line item under a header. <c>Id</c>/<c>SortOrder</c> are response-only.</summary>
public sealed record LiftdeskChecklistItem(
    Guid Id,
    string Text,
    int SortOrder,
    bool IsActive);

/// <summary>A checklist header (group) with its ordered items. <c>Id</c>/<c>SortOrder</c> are response-only.</summary>
public sealed record LiftdeskChecklistHeader(
    Guid Id,
    string Title,
    int SortOrder,
    bool IsActive,
    List<LiftdeskChecklistItem> Items);

/// <summary>
/// A company's full checklist document for one kind ("maintenance" | "fault").
/// <c>Headers</c> come sorted by SortOrder and include inactive (but not deleted) rows.
/// </summary>
public sealed record LiftdeskChecklistDoc(
    int CompanyId,
    string Kind,
    int FormId,
    List<LiftdeskChecklistHeader> Headers);

/// <summary>Item input for the full-document PUT. <c>IsActive</c> defaults to true on the Liftdesk side.</summary>
public sealed record LiftdeskChecklistItemInput(
    string Text,
    bool IsActive = true);

/// <summary>
/// Header input for the full-document PUT. Order in the array becomes the SortOrder.
/// <c>IsActive</c> is last so it can default to true (contract: optional, default true) when omitted.
/// </summary>
public sealed record LiftdeskChecklistHeaderInput(
    string Title,
    List<LiftdeskChecklistItemInput> Items,
    bool IsActive = true);

/// <summary>
/// Body of PUT …/{kind}-checklist. This is a FULL-document replace: the sent set becomes the new
/// checklist (an empty list intentionally clears it). No ids / sortOrders are sent.
/// </summary>
public sealed record LiftdeskChecklistUpdateRequest(
    List<LiftdeskChecklistHeaderInput> Headers);

/// <summary>
/// Response of POST …/checklists/reset. <c>Maintenance</c>/<c>Fault</c> are null when that kind
/// was not part of the reset scope.
/// </summary>
public sealed record LiftdeskChecklistResetResponse(
    int CompanyId,
    LiftdeskChecklistDoc? Maintenance,
    LiftdeskChecklistDoc? Fault);
