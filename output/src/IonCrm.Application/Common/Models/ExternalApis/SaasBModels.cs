namespace IonCrm.Application.Common.Models.ExternalApis;

// ── SaaS B — Different endpoint structure, X-Api-Key auth ───────────────────

/// <summary>SaaS B GET /customers/list response envelope.</summary>
public record SaasBCustomersResponse(List<SaasBCustomer> Customers, int Count);

/// <summary>A single customer record as returned by SaaS B (different field names).</summary>
public record SaasBCustomer(
    string CustomerId,
    string FullName,
    string? ContactEmail,
    string? Mobile,
    string? StreetAddress,
    string? TaxId,
    string AccountState,  // "ACTIVE" | "LEAD" | "INACTIVE" | "CHURNED"
    string? Tier,         // "ENTERPRISE" | "SME" | "INDIVIDUAL"
    string? OwnerId,
    long CreatedTimestamp,
    long UpdatedTimestamp);

/// <summary>SaaS B GET /subscriptions/all response envelope.</summary>
public record SaasBSubscriptionsResponse(List<SaasBSubscription> Subscriptions, int Count);

/// <summary>A single subscription record as returned by SaaS B.</summary>
public record SaasBSubscription(
    string SubId,
    string ClientId,
    string ProductName,
    bool IsActive,
    decimal Price,
    string CurrencyCode,
    long StartTimestamp,
    long? EndTimestamp,
    long UpdatedTimestamp);

/// <summary>SaaS B GET /orders/all response envelope.</summary>
public record SaasBOrdersResponse(List<SaasBOrder> Orders, int Count);

/// <summary>A single order record as returned by SaaS B.</summary>
public record SaasBOrder(
    string OrderId,
    string ClientId,
    string? SubId,
    decimal TotalAmount,
    string Currency,
    string OrderStatus,   // "PENDING" | "PAID" | "REFUNDED"
    long OrderTimestamp,
    long UpdatedTimestamp);

/// <summary>Payload sent to SaaS B webhook endpoint on CRM events.</summary>
public record SaasBCallbackPayload(
    string Event,          // "crm.subscription_extended" | "crm.status_changed"
    string Id,
    string Type,           // "customer" | "subscription"
    string Project,
    object Payload,
    long Timestamp);

/// <summary>Incoming webhook payload pushed by SaaS B to POST /api/v1/sync/saas-b.</summary>
public record SaasBWebhookPayload(
    string Event,
    string Id,
    string Type,
    object Payload,
    long Timestamp,
    string? HmacSignature); // HMAC signature for verification
