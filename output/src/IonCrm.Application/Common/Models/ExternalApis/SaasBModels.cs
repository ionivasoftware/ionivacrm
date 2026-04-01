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

// ── Rezerval new CRM API — https://rezback.rezerval.com ─────────────────────

/// <summary>
/// A single company record as returned by Rezerval GET /v1/Crm/CompanyList.
/// Note: "ExperationDate" is the field name used by the Rezerval API (typo preserved).
/// </summary>
public record RezervalCompany(
    int Id,
    string Name,
    string? Title,
    string? Phone,
    string? Email,
    string? Logo,
    DateTime ExperationDate,
    DateTime CreatedOn,
    bool IsDeleted,
    bool IsActiveOnline);

// ── RezervAl Company Create/Update ───────────────────────────────────────────

/// <summary>
/// Form data for creating or updating a company in RezervAl.
/// Sent as multipart/form-data to POST/PUT https://rezback.rezerval.com/v1/Crm/Company.
/// </summary>
public class RezervalCompanyFormData
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TaxUnit { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string? TCNo { get; set; }
    public bool IsPersonCompany { get; set; }
    public string Address { get; set; } = string.Empty;
    public int Language { get; set; } = 1;
    public int CountryPhoneCode { get; set; } = 90;
    public DateTime? ExperationDate { get; set; }
    public string AdminNameSurname { get; set; } = string.Empty;
    public string AdminLoginName { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPhone { get; set; } = string.Empty;
    /// <summary>Raw bytes of the logo image file. Null means no logo is sent.</summary>
    public byte[]? LogoBytes { get; set; }
    /// <summary>Original file name of the logo (e.g. "logo.png"). Used as the form field file name.</summary>
    public string? LogoFileName { get; set; }
}

/// <summary>Response from RezervAl POST /v1/Crm/Company on successful creation.</summary>
public record RezervalCreateCompanyResponse(int CompanyId, string? Message);
