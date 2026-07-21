namespace IonCrm.Application.Common.Models.ExternalApis;

// ── Liftdesk (EMS) pricing management API ────────────────────────────────────
// Base: {LiftdeskBaseUrl}/api/v1/crm/pricing, auth "Authorization: Bearer {LiftdeskApiKey}".
// Responses use the shared LiftdeskEnvelope<T> ({ success, data, message, errors, statusCode }).
// Prices are NET TL (VAT-exclusive); priceYearly is the yearly TOTAL. maxUsers/maxElevators 0 = unlimited.

/// <summary>A subscription plan (fixed tier) returned by GET /api/v1/crm/pricing/plans.</summary>
public record LiftdeskPricingPlan(
    Guid Id,
    string Name,
    string? Tier,                              // Standart | Pro | Prime — read-only (feature gating)
    string? Description,
    decimal PriceMonthly,
    decimal PriceYearly,                       // yearly TOTAL, not monthly-equivalent
    int MaxUsers,                              // 0 = unlimited
    int MaxElevators,                          // 0 = unlimited
    bool IsActive,
    string? IyzicoProductReferenceCode,        // read-only (iyzico webhook matching)
    string? IyzicoPlanReferenceCodeMonthly,
    string? IyzicoPlanReferenceCodeYearly,
    DateTime? CreatedAt);

/// <summary>An SMS credit package returned by GET /api/v1/crm/pricing/sms-packages.</summary>
public record LiftdeskSmsPackage(
    Guid Id,
    string Name,
    int SmsCount,                              // total credits topped up on purchase
    decimal Price,
    bool IsActive,
    DateTime? CreatedAt);

// ── Request bodies (CRM → Liftdesk pricing API) ──────────────────────────────

/// <summary>Body for PUT /plans/{id}. Full replace — tier and iyzico codes are NOT editable from CRM.</summary>
public record UpdatePricingPlanRequest(
    string Name,
    string? Description,
    decimal PriceMonthly,
    decimal PriceYearly,
    int MaxUsers,
    int MaxElevators,
    bool IsActive);

/// <summary>Body for POST /sms-packages.</summary>
public record CreateSmsPackageRequest(
    string Name,
    int SmsCount,
    decimal Price);

/// <summary>Body for PUT /sms-packages/{id}. <c>IsActive</c> defaults to true when omitted.</summary>
public record UpdateSmsPackageRequest(
    string Name,
    int SmsCount,
    decimal Price,
    bool IsActive = true);
