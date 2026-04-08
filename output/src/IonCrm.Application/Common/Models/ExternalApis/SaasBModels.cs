using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("id")] int Id,
    string Name,
    string? Title,
    string? Phone,
    string? Email,
    string? Logo,
    DateTime ExperationDate,
    DateTime CreatedOn,
    bool IsDeleted,
    bool? IsActiveOnline);

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

/// <summary>Envelope wrapper returned by RezervAl POST /v1/Crm/Company.</summary>
public record RezervalCreateCompanyEnvelope(RezervalCreateCompanyData? Data, bool IsSuccess, string? Message);

/// <summary>Data payload inside <see cref="RezervalCreateCompanyEnvelope"/>.</summary>
public record RezervalCreateCompanyData(int CompanyId, string? Message);

/// <summary>Resolved create result after unwrapping the envelope.</summary>
public record RezervalCreateCompanyResponse(int CompanyId, string? Message);

/// <summary>Response from RezervAl POST /v1/Token/GetToken.</summary>
public record RezervalTokenResponse(RezervalTokenData? Data, bool IsSuccess, string? Message);

/// <summary>Token data nested inside <see cref="RezervalTokenResponse"/>.</summary>
public record RezervalTokenData(string Token);

/// <summary>Envelope wrapper for Rezerval GET /v1/Crm/CompanyList response.</summary>
public record RezervalCompanyListResponse(List<RezervalCompany>? Data, bool IsSuccess, string? Message);

// ── RezervAl Subscription Create (iyzico-backed) ─────────────────────────────

/// <summary>
/// JSON request body sent to the RezervAl subscription endpoint.
/// RezervAl creates an iyzico subscription + payment plan from this and returns the resulting refs.
/// </summary>
public record RezervalSubscriptionRequest(
    [property: JsonPropertyName("rezervalCompanyId")] int RezervalCompanyId,
    [property: JsonPropertyName("subscriptionName")] string SubscriptionName,
    [property: JsonPropertyName("monthlyAmount")] decimal MonthlyAmount,
    [property: JsonPropertyName("paymentType")] string PaymentType,   // "CreditCard" | "EftWire"
    [property: JsonPropertyName("startDate")] string StartDate,        // "yyyy-MM-dd"
    [property: JsonPropertyName("durationMonths")] int? DurationMonths,
    [property: JsonPropertyName("currency")] string Currency = "TRY");

/// <summary>Envelope wrapper returned by the RezervAl subscription endpoint.</summary>
public record RezervalSubscriptionResponse(
    [property: JsonPropertyName("data")] RezervalSubscriptionData? Data,
    [property: JsonPropertyName("isSuccess")] bool IsSuccess,
    [property: JsonPropertyName("message")] string? Message);

/// <summary>Data payload inside <see cref="RezervalSubscriptionResponse"/>.</summary>
public record RezervalSubscriptionData(
    [property: JsonPropertyName("rezervalSubscriptionId")] string? RezervalSubscriptionId,
    [property: JsonPropertyName("rezervalPaymentPlanId")] string? RezervalPaymentPlanId,
    [property: JsonPropertyName("message")] string? Message);

// ── RezervAl Company Summary ─────────────────────────────────────────────────

/// <summary>
/// Envelope wrapper returned by GET https://rezback.rezerval.com/v1/Crm/CompanySummary?companyId={id}
/// </summary>
public record RezervalCompanySummaryResponse(
    [property: JsonPropertyName("data")] RezervalCompanySummary? Data,
    [property: JsonPropertyName("isSuccess")] bool IsSuccess,
    [property: JsonPropertyName("message")] string? Message);

/// <summary>Top-level summary payload returned by Rezerval CompanySummary endpoint.</summary>
public record RezervalCompanySummary(
    [property: JsonPropertyName("companyId")] int CompanyId,
    [property: JsonPropertyName("companyName")] string? CompanyName,
    [property: JsonPropertyName("lastWeek")] RezervalSummaryPeriod? LastWeek,
    [property: JsonPropertyName("lastMonth")] RezervalSummaryPeriod? LastMonth,
    [property: JsonPropertyName("last3Months")] RezervalSummaryPeriod? Last3Months);

/// <summary>Aggregated metrics for a single time-window in the company summary.</summary>
public record RezervalSummaryPeriod(
    [property: JsonPropertyName("startDate")] DateTime? StartDate,
    [property: JsonPropertyName("endDate")] DateTime? EndDate,
    [property: JsonPropertyName("reservationCount")] int ReservationCount,
    [property: JsonPropertyName("personCount")] int PersonCount,
    [property: JsonPropertyName("completedReservationCount")] int CompletedReservationCount,
    [property: JsonPropertyName("cancelledReservationCount")] int CancelledReservationCount,
    [property: JsonPropertyName("onlineReservationCount")] int OnlineReservationCount,
    [property: JsonPropertyName("walkInCount")] int WalkInCount,
    [property: JsonPropertyName("walkInPersonCount")] int WalkInPersonCount,
    [property: JsonPropertyName("smsSentCount")] int SmsSentCount);
