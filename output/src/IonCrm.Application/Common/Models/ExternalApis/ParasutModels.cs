using System.Text.Json;
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
    [property: JsonPropertyName("invoice_series")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InvoiceSeries,
    [property: JsonPropertyName("invoice_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? InvoiceId,
    [property: JsonPropertyName("currency")]           string Currency,         // "TRL"
    [property: JsonPropertyName("exchange_rate")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExchangeRate,
    [property: JsonPropertyName("withholding_rate")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? WithholdingRate,
    [property: JsonPropertyName("vat_withholding_rate")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? VatWithholdingRate,
    [property: JsonPropertyName("invoice_discount_type")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InvoiceDiscountType,
    [property: JsonPropertyName("invoice_discount")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InvoiceDiscount,
    [property: JsonPropertyName("billing_address")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BillingAddress,
    [property: JsonPropertyName("billing_phone")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BillingPhone,
    [property: JsonPropertyName("billing_fax")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BillingFax,
    [property: JsonPropertyName("tax_office")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TaxOffice,
    [property: JsonPropertyName("tax_number")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TaxNumber,
    [property: JsonPropertyName("city")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? City,
    [property: JsonPropertyName("district")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? District,
    [property: JsonPropertyName("payment_account_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? PaymentAccountId,
    [property: JsonPropertyName("is_abroad")]          bool IsAbroad = false,
    // Read-only fields returned by the API — ignored when writing (POST/PATCH).
    // NOTE: Paraşüt returns these numeric fields as JSON strings (e.g. "1234.56"), not numbers.
    [property: JsonPropertyName("net_total")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NetTotal = null,
    [property: JsonPropertyName("gross_total")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GrossTotal = null,
    [property: JsonPropertyName("total_paid")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TotalPaid = null,
    [property: JsonPropertyName("remaining")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Remaining = null,
    [property: JsonPropertyName("archiving_status")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ArchivingStatus = null
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

    /// <summary>
    /// Optional product relationship. When set, links this line item to a Paraşüt product (ürün)
    /// so that product-level accounting rules (income/expense accounts) are applied automatically.
    /// </summary>
    [JsonPropertyName("relationships")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SalesInvoiceDetailRelationships? Relationships { get; set; }
}

/// <summary>Relationship section for a sales invoice detail (line item).</summary>
public class SalesInvoiceDetailRelationships
{
    [JsonPropertyName("product")]
    public ProductRelationship? Product { get; set; }
}

/// <summary>JSON:API relationship pointing to a Paraşüt product (ürün).</summary>
public class ProductRelationship
{
    [JsonPropertyName("data")]
    public ProductRelationshipData? Data { get; set; }
}

/// <summary>Resource identifier for a Paraşüt product.</summary>
public class ProductRelationshipData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "products";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
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

/// <summary>
/// Attributes returned by the e-invoice inbox lookup endpoint.
/// GET /v4/{company_id}/e_invoice_inboxes?filter[vkn]={vkn}
/// Returns registered e-invoice addresses for a given tax number.
/// </summary>
public record ParasutEInvoiceInboxAttributes(
    [property: JsonPropertyName("vkn")]                   string? Vkn,
    [property: JsonPropertyName("e_invoice_address")]     string? EInvoiceAddress,
    [property: JsonPropertyName("name")]                  string? Name,
    [property: JsonPropertyName("inbox_type")]            string? InboxType,          // "PK" | "GB"
    [property: JsonPropertyName("address_registered_at")] string? AddressRegisteredAt,
    [property: JsonPropertyName("registered_at")]         string? RegisteredAt,
    [property: JsonPropertyName("created_at")]            string? CreatedAt,
    [property: JsonPropertyName("updated_at")]            string? UpdatedAt
);

// ── E-Invoice / E-Archive Officialize Requests ──────────────────────────────

/// <summary>
/// Request body for creating an e-invoice (POST /v4/{company_id}/e_invoices)
/// or e-archive (POST /v4/{company_id}/e_archives).
/// Both endpoints use the same JSON:API relationship structure pointing to a sales_invoice.
/// </summary>
public class CreateEDocumentRequest
{
    [JsonPropertyName("data")]
    public CreateEDocumentData Data { get; set; } = new();
}

public class CreateEDocumentData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "e_invoices"; // "e_invoices" or "e_archives"

    [JsonPropertyName("relationships")]
    public EDocumentRelationships Relationships { get; set; } = new();
}

public class EDocumentRelationships
{
    [JsonPropertyName("invoice")]
    public EDocumentInvoiceRelationship Invoice { get; set; } = new();
}

public class EDocumentInvoiceRelationship
{
    [JsonPropertyName("data")]
    public EDocumentInvoiceRelationshipData Data { get; set; } = new();
}

public class EDocumentInvoiceRelationshipData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "sales_invoices";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

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
    [property: JsonPropertyName("code")]                  string? Code,
    [property: JsonPropertyName("name")]                  string Name,
    // Paraşüt may return vat_rate as a JSON number (20) or string ("20").
    // JsonElement handles both; use VatRateInt helper to get the integer value.
    [property: JsonPropertyName("vat_rate")]              JsonElement? VatRate,
    // Paraşüt returns price as a JSON string ("100.00") not a number.
    // Different product types may use different field names.
    [property: JsonPropertyName("sales_price")]           string? SalesPrice,
    [property: JsonPropertyName("list_price")]            string? ListPrice,
    [property: JsonPropertyName("sales_price_in_trl")]    string? SalesPriceInTrl,
    [property: JsonPropertyName("sales_excise_duty_code")] string? SalesExciseDutyCode,
    [property: JsonPropertyName("unit")]                  string? Unit,
    [property: JsonPropertyName("currency")]              string? Currency
)
{
    /// <summary>
    /// Returns vat_rate as an integer (e.g. 20 for 20%) regardless of whether
    /// Paraşüt sent it as a JSON number or a JSON string.
    /// Returns null if the field is absent or unparseable.
    /// </summary>
    public int? VatRateInt =>
        VatRate is { } el
            ? el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) ? n
            : el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s) ? s
            : (int?)null
            : null;
}
