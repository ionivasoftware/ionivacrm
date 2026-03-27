using System.Text.Json.Serialization;

namespace IonCrm.Application.Common.Models.ExternalApis;

// ─────────────────────────────────────────────────────────────────────────────
//  Paraşüt API v4 — Models
//
//  Paraşüt implements the JSON:API (https://jsonapi.org) specification.
//  All requests and responses wrap data in { "data": { "type": "...", ... } }.
//  Base URL: https://api.parasut.com/v4/{company_id}/
//  Auth:     OAuth 2.0 password grant → Bearer token
// ─────────────────────────────────────────────────────────────────────────────

// ── OAuth ─────────────────────────────────────────────────────────────────────

/// <summary>Token request payload for OAuth 2.0 password grant.</summary>
public record ParasutTokenRequest(
    [property: JsonPropertyName("grant_type")]     string GrantType,
    [property: JsonPropertyName("client_id")]      string ClientId,
    [property: JsonPropertyName("client_secret")]  string ClientSecret,
    [property: JsonPropertyName("username")]        string Username,
    [property: JsonPropertyName("password")]        string Password,
    [property: JsonPropertyName("redirect_uri")]    string RedirectUri = "urn:ietf:wg:oauth:2.0:oob"
);

/// <summary>Token request payload for OAuth 2.0 refresh_token grant.</summary>
public record ParasutRefreshTokenRequest(
    [property: JsonPropertyName("grant_type")]     string GrantType,
    [property: JsonPropertyName("client_id")]      string ClientId,
    [property: JsonPropertyName("client_secret")]  string ClientSecret,
    [property: JsonPropertyName("refresh_token")]  string RefreshToken,
    [property: JsonPropertyName("redirect_uri")]   string RedirectUri = "urn:ietf:wg:oauth:2.0:oob"
);

/// <summary>Token response from POST /oauth/token.</summary>
public record ParasutTokenResponse(
    [property: JsonPropertyName("access_token")]  string AccessToken,
    [property: JsonPropertyName("token_type")]    string TokenType,
    [property: JsonPropertyName("expires_in")]    int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("created_at")]    long CreatedAt
);

// ── JSON:API Wrappers ─────────────────────────────────────────────────────────

/// <summary>JSON:API data object for a single resource.</summary>
public record JsonApiDataObject<TAttributes>(
    [property: JsonPropertyName("id")]         string? Id,
    [property: JsonPropertyName("type")]       string Type,
    [property: JsonPropertyName("attributes")] TAttributes Attributes
);

/// <summary>JSON:API request body wrapping a single resource (for POST/PATCH).</summary>
public record JsonApiRequest<TAttributes>(
    [property: JsonPropertyName("data")] JsonApiDataObject<TAttributes> Data
);

/// <summary>JSON:API response wrapping a single resource.</summary>
public record JsonApiResponse<TAttributes>(
    [property: JsonPropertyName("data")] JsonApiDataObject<TAttributes> Data
);

/// <summary>JSON:API response wrapping a list of resources.</summary>
public record JsonApiListResponse<TAttributes>(
    [property: JsonPropertyName("data")] List<JsonApiDataObject<TAttributes>> Data,
    [property: JsonPropertyName("meta")] ParasutMeta? Meta
);

/// <summary>Pagination metadata returned in list responses.</summary>
public record ParasutMeta(
    [property: JsonPropertyName("total_count")]  int TotalCount,
    [property: JsonPropertyName("total_pages")]  int TotalPages,
    [property: JsonPropertyName("current_page")] int CurrentPage,
    [property: JsonPropertyName("per_page")]     int PerPage
);

// ── Contact (Cari) ────────────────────────────────────────────────────────────

/// <summary>
/// Attributes for a Paraşüt contact (cari).
/// <para>contact_type: "person" | "company"</para>
/// <para>account_type: "customer" | "supplier" | "both"</para>
/// </summary>
public record ParasutContactAttributes(
    [property: JsonPropertyName("name")]          string Name,
    [property: JsonPropertyName("email")]         string? Email,
    [property: JsonPropertyName("phone")]         string? Phone,
    [property: JsonPropertyName("contact_type")]  string ContactType,   // "person" | "company"
    [property: JsonPropertyName("account_type")]  string AccountType,   // "customer" | "supplier" | "both"
    [property: JsonPropertyName("tax_number")]    string? TaxNumber,
    [property: JsonPropertyName("tax_office")]    string? TaxOffice,
    [property: JsonPropertyName("address")]       string? Address,
    [property: JsonPropertyName("city")]          string? City,
    [property: JsonPropertyName("district")]      string? District,
    [property: JsonPropertyName("country")]       string? Country = "Türkiye",
    [property: JsonPropertyName("is_abroad")]     bool IsAbroad = false
);

// ── Sales Invoice (Satış Faturası) ────────────────────────────────────────────

/// <summary>
/// Attributes for a Paraşüt sales invoice.
/// item_type: "invoice" | "export" | "proforma" | "purchase_bill" | "refund"
/// currency:  "TRL" | "USD" | "EUR" | "GBP"
/// </summary>
public record ParasutSalesInvoiceAttributes(
    [property: JsonPropertyName("item_type")]          string ItemType,
    [property: JsonPropertyName("description")]        string? Description,
    [property: JsonPropertyName("issue_date")]         string IssueDate,       // yyyy-MM-dd
    [property: JsonPropertyName("due_date")]           string DueDate,         // yyyy-MM-dd
    [property: JsonPropertyName("invoice_series")]     string? InvoiceSeries,
    [property: JsonPropertyName("invoice_id")]         int? InvoiceId,
    [property: JsonPropertyName("currency")]           string Currency,         // "TRL"
    [property: JsonPropertyName("exchange_rate")]      string? ExchangeRate,
    [property: JsonPropertyName("withholding_rate")]   string? WithholdingRate,
    [property: JsonPropertyName("vat_withholding_rate")] string? VatWithholdingRate,
    [property: JsonPropertyName("invoice_discount_type")] string? InvoiceDiscountType,
    [property: JsonPropertyName("invoice_discount")]   string? InvoiceDiscount,
    [property: JsonPropertyName("billing_address")]    string? BillingAddress,
    [property: JsonPropertyName("billing_phone")]      string? BillingPhone,
    [property: JsonPropertyName("billing_fax")]        string? BillingFax,
    [property: JsonPropertyName("tax_office")]         string? TaxOffice,
    [property: JsonPropertyName("tax_number")]         string? TaxNumber,
    [property: JsonPropertyName("city")]               string? City,
    [property: JsonPropertyName("district")]           string? District,
    [property: JsonPropertyName("payment_account_id")] int? PaymentAccountId,
    [property: JsonPropertyName("is_abroad")]          bool IsAbroad = false,
    // Read-only fields returned by the API:
    [property: JsonPropertyName("net_total")]          decimal? NetTotal = null,
    [property: JsonPropertyName("gross_total")]        decimal? GrossTotal = null,
    [property: JsonPropertyName("total_paid")]         decimal? TotalPaid = null,
    [property: JsonPropertyName("remaining")]          decimal? Remaining = null,
    [property: JsonPropertyName("archiving_status")]   string? ArchivingStatus = null
);

/// <summary>A single line item (kalem) within a sales invoice.</summary>
public record ParasutSalesInvoiceDetailAttributes(
    [property: JsonPropertyName("quantity")]        decimal Quantity,
    [property: JsonPropertyName("unit_price")]      decimal UnitPrice,
    [property: JsonPropertyName("vat_rate")]        int VatRate,             // 0, 1, 8, 10, 20
    [property: JsonPropertyName("discount_type")]   string DiscountType,    // "percentage" | "amount"
    [property: JsonPropertyName("discount_value")]  decimal DiscountValue,
    [property: JsonPropertyName("description")]     string? Description,
    [property: JsonPropertyName("unit")]            string? Unit = "Adet"
);

/// <summary>
/// Full request body for creating a sales invoice.
/// Includes line items and contact relationship per JSON:API spec.
/// </summary>
public class CreateSalesInvoiceRequest
{
    [JsonPropertyName("data")]
    public CreateSalesInvoiceData Data { get; set; } = new();
}

public class CreateSalesInvoiceData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "sales_invoices";

    [JsonPropertyName("attributes")]
    public ParasutSalesInvoiceAttributes Attributes { get; set; } = null!;

    [JsonPropertyName("relationships")]
    public CreateSalesInvoiceRelationships Relationships { get; set; } = new();
}

public class CreateSalesInvoiceRelationships
{
    [JsonPropertyName("details")]
    public SalesInvoiceDetailsRelationship Details { get; set; } = new();

    [JsonPropertyName("contact")]
    public ContactRelationship? Contact { get; set; }

    [JsonPropertyName("sales_offer")]
    public ContactRelationship? SalesOffer { get; set; }
}

public class SalesInvoiceDetailsRelationship
{
    [JsonPropertyName("data")]
    public List<SalesInvoiceDetailData> Data { get; set; } = new();
}

public class SalesInvoiceDetailData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "sales_invoice_details";

    [JsonPropertyName("attributes")]
    public ParasutSalesInvoiceDetailAttributes Attributes { get; set; } = null!;
}

public class ContactRelationship
{
    [JsonPropertyName("data")]
    public ContactRelationshipData? Data { get; set; }
}

public class ContactRelationshipData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "contacts";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

// ── Payment (Ödeme) ───────────────────────────────────────────────────────────

/// <summary>Attributes for recording a payment against an invoice.</summary>
public record ParasutPaymentAttributes(
    [property: JsonPropertyName("amount")]          decimal Amount,
    [property: JsonPropertyName("date")]            string Date,            // yyyy-MM-dd
    [property: JsonPropertyName("currency")]        string Currency,        // "TRL"
    [property: JsonPropertyName("currency_rate")]   decimal CurrencyRate,
    [property: JsonPropertyName("notes")]           string? Notes
);

/// <summary>Full request body for paying a sales invoice.</summary>
public class CreatePaymentRequest
{
    [JsonPropertyName("data")]
    public CreatePaymentData Data { get; set; } = new();
}

public class CreatePaymentData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "payments";

    [JsonPropertyName("attributes")]
    public ParasutPaymentAttributes Attributes { get; set; } = null!;

    [JsonPropertyName("relationships")]
    public PaymentRelationships Relationships { get; set; } = new();
}

public class PaymentRelationships
{
    [JsonPropertyName("account")]
    public AccountRelationship Account { get; set; } = new();
}

public class AccountRelationship
{
    [JsonPropertyName("data")]
    public AccountRelationshipData Data { get; set; } = new();
}

public class AccountRelationshipData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "bank_accounts";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

// ── E-Invoice (e-Fatura / e-Arşiv) ───────────────────────────────────────────

/// <summary>Attributes returned for an e-invoice or e-archive request.</summary>
public record ParasutEInvoiceAttributes(
    [property: JsonPropertyName("uuid")]            string? Uuid,
    [property: JsonPropertyName("status")]          string? Status,
    [property: JsonPropertyName("scenario")]        string? Scenario,       // "commercial" | "basic"
    [property: JsonPropertyName("note")]            string? Note,
    [property: JsonPropertyName("vkn_tckn")]        string? VknTckn,
    [property: JsonPropertyName("to")]              string? To,
    [property: JsonPropertyName("errors")]          string? Errors
);

// ── Account (Kasa / Banka) ────────────────────────────────────────────────────

/// <summary>Attributes for a Paraşüt bank account or cash register.</summary>
public record ParasutAccountAttributes(
    [property: JsonPropertyName("name")]            string Name,
    [property: JsonPropertyName("currency")]        string Currency,
    [property: JsonPropertyName("account_type")]    string AccountType,     // "bank_account" | "cash" | "pos"
    [property: JsonPropertyName("bank_name")]       string? BankName,
    [property: JsonPropertyName("bank_branch")]     string? BankBranch,
    [property: JsonPropertyName("bank_account_no")] string? BankAccountNo,
    [property: JsonPropertyName("iban")]            string? Iban,
    [property: JsonPropertyName("balance")]         decimal? Balance
);

// ── Product (Ürün / Hizmet) ───────────────────────────────────────────────────

/// <summary>Attributes for a Paraşüt product or service.</summary>
public record ParasutProductAttributes(
    [property: JsonPropertyName("code")]         string? Code,
    [property: JsonPropertyName("name")]         string Name,
    [property: JsonPropertyName("vat_rate")]     int VatRate,
    [property: JsonPropertyName("sales_price")]  decimal? SalesPrice,
    [property: JsonPropertyName("sales_excise_duty_code")] string? SalesExciseDutyCode,
    [property: JsonPropertyName("unit")]         string? Unit,
    [property: JsonPropertyName("currency")]     string? Currency
);
