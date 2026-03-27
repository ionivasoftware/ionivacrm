namespace IonCrm.Application.Common.Models.ExternalApis;

// ── SaaS A — REST API under /api/v1/ with Bearer token ──────────────────────

/// <summary>SaaS A GET /api/v1/customers response envelope.</summary>
public record SaasACustomersResponse(List<SaasACustomer> Data, int Total);

/// <summary>A single customer record as returned by SaaS A.</summary>
public record SaasACustomer(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    string Status,        // "active" | "lead" | "inactive" | "churned"
    string? Segment,      // "enterprise" | "sme" | "individual"
    string? AssignedUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>SaaS A GET /api/v1/subscriptions response envelope.</summary>
public record SaasASubscriptionsResponse(List<SaasASubscription> Data, int Total);

/// <summary>A single subscription record as returned by SaaS A.</summary>
public record SaasASubscription(
    string Id,
    string CustomerId,
    string Plan,
    string Status,         // "active" | "expired" | "cancelled"
    decimal Amount,
    string Currency,
    DateTime StartDate,
    DateTime? ExpiresAt,
    DateTime UpdatedAt);

/// <summary>SaaS A GET /api/v1/orders response envelope.</summary>
public record SaasAOrdersResponse(List<SaasAOrder> Data, int Total);

/// <summary>A single order record as returned by SaaS A.</summary>
public record SaasAOrder(
    string Id,
    string CustomerId,
    string? SubscriptionId,
    decimal Amount,
    string Currency,
    string Status,         // "pending" | "paid" | "refunded"
    DateTime OrderDate,
    DateTime UpdatedAt);

/// <summary>Payload sent to SaaS A callback endpoint on CRM events.</summary>
public record SaasACallbackPayload(
    string EventType,      // "subscription_extended" | "status_changed" | "customer_updated"
    string EntityType,     // "customer" | "subscription"
    string EntityId,
    string ProjectId,
    object Data,
    DateTime OccurredAt);

// ── EMS CRM endpoint — /api/v1/crm/customers ─────────────────────────────────

/// <summary>Paginated response from EMS GET /api/v1/crm/customers.</summary>
public record EmsCrmCustomersResponse(
    List<EmsCrmCustomer> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);

/// <summary>A single customer record from the EMS CRM customers endpoint.</summary>
public record EmsCrmCustomer(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    string? Segment,
    DateTime CreatedOn,
    DateTime UpdatedAt,
    DateTime? ExpirationDate);

/// <summary>Incoming webhook payload pushed by SaaS A to POST /api/v1/sync/saas-a.</summary>
public record SaasAWebhookPayload(
    string EventType,
    string EntityType,
    string EntityId,
    object Data,
    DateTime OccurredAt,
    string? Signature);    // HMAC signature for verification

// ── EMS extend expiration ─────────────────────────────────────────────────────

/// <summary>Response from POST /api/v1/crm/companies/{id}/extend-expiration.</summary>
public record EmsExtendExpirationResponse(
    int CompanyId,
    DateTime ExpirationDate,
    EmsExtendDuration Extended);

/// <summary>Duration details returned inside <see cref="EmsExtendExpirationResponse"/>.</summary>
public record EmsExtendDuration(string DurationType, int Amount);
