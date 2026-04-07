using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// HTTP client interface for Paraşüt accounting API v4.
/// Base URL: https://api.parasut.com/v4/
///
/// All methods that access company-specific resources require a valid Bearer
/// <paramref name="accessToken"/> and the numeric <paramref name="companyId"/>.
///
/// Callers are responsible for ensuring the token is valid before calling.
/// Use <see cref="GetTokenAsync"/> or <see cref="RefreshTokenAsync"/> to obtain a token.
/// </summary>
public interface IParasutClient
{
    // ── OAuth ─────────────────────────────────────────────────────────────────

    /// <summary>Obtains an access token using OAuth 2.0 password grant.</summary>
    Task<ParasutTokenResponse> GetTokenAsync(
        ParasutTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Obtains a new access token using the refresh token grant.</summary>
    Task<ParasutTokenResponse> RefreshTokenAsync(
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);

    // ── Contacts (Cariler) ────────────────────────────────────────────────────

    /// <summary>Returns a paginated list of contacts for the given company.</summary>
    /// <param name="search">Optional server-side name filter (Paraşüt filter[name]).</param>
    Task<JsonApiListResponse<ParasutContactAttributes>> GetContactsAsync(
        string accessToken,
        long companyId,
        int page = 1,
        int pageSize = 25,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single contact by its Paraşüt ID.</summary>
    Task<JsonApiResponse<ParasutContactAttributes>> GetContactByIdAsync(
        string accessToken,
        long companyId,
        string contactId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new contact (cari) in Paraşüt.</summary>
    Task<JsonApiResponse<ParasutContactAttributes>> CreateContactAsync(
        string accessToken,
        long companyId,
        ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing contact (cari) in Paraşüt.</summary>
    Task<JsonApiResponse<ParasutContactAttributes>> UpdateContactAsync(
        string accessToken,
        long companyId,
        string contactId,
        ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default);

    // ── Sales Invoices (Satış Faturaları) ─────────────────────────────────────

    /// <summary>Returns a paginated list of sales invoices for the given company.</summary>
    Task<JsonApiListResponse<ParasutSalesInvoiceAttributes>> GetSalesInvoicesAsync(
        string accessToken,
        long companyId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single sales invoice by its Paraşüt ID.</summary>
    Task<JsonApiResponse<ParasutSalesInvoiceAttributes>> GetSalesInvoiceByIdAsync(
        string accessToken,
        long companyId,
        string invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new sales invoice including line items and optional contact link.</summary>
    Task<JsonApiResponse<ParasutSalesInvoiceAttributes>> CreateSalesInvoiceAsync(
        string accessToken,
        long companyId,
        CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records a payment against a sales invoice.</summary>
    Task<JsonApiResponse<ParasutPaymentAttributes>> PaySalesInvoiceAsync(
        string accessToken,
        long companyId,
        string invoiceId,
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default);

    // ── Products (Ürünler / Hizmetler) ────────────────────────────────────────

    /// <summary>Returns a paginated list of products/services for the given company.</summary>
    Task<JsonApiListResponse<ParasutProductAttributes>> GetProductsAsync(
        string accessToken,
        long companyId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single product/service by its Paraşüt ID.</summary>
    Task<JsonApiResponse<ParasutProductAttributes>> GetProductByIdAsync(
        string accessToken,
        long companyId,
        string productId,
        CancellationToken cancellationToken = default);

    // ── Accounts (Kasalar / Banka Hesapları) ──────────────────────────────────

    /// <summary>Returns all bank accounts and cash registers for the given company.</summary>
    Task<JsonApiListResponse<ParasutAccountAttributes>> GetAccountsAsync(
        string accessToken,
        long companyId,
        CancellationToken cancellationToken = default);

    // ── Contact Invoices (Cari Hareketleri) ───────────────────────────────────

    /// <summary>Returns a paginated list of sales invoices filtered by a specific contact (cari).</summary>
    Task<JsonApiListResponse<ParasutSalesInvoiceAttributes>> GetContactInvoicesAsync(
        string accessToken,
        long companyId,
        string contactId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    // ── E-Invoice Inbox (e-Fatura Mükellef Sorgu) ──────────────────────────

    /// <summary>
    /// Queries the e-invoice inbox registry by tax number (VKN).
    /// Returns matching inboxes if the tax number is registered for e-invoicing.
    /// An empty list means the entity is NOT an e-invoice payer.
    /// </summary>
    Task<JsonApiListResponse<ParasutEInvoiceInboxAttributes>> GetEInvoiceInboxesAsync(
        string accessToken,
        long companyId,
        string vkn,
        CancellationToken cancellationToken = default);

    // ── E-Invoice / E-Archive Officialize ───────────────────────────────────

    /// <summary>
    /// Creates an e-invoice for a sales invoice that has already been created in Paraşüt.
    /// Used for customers who are registered e-invoice payers (IsEInvoicePayer=true).
    /// POST /v4/{company_id}/e_invoices
    /// </summary>
    Task<JsonApiResponse<ParasutEInvoiceAttributes>> CreateEInvoiceAsync(
        string accessToken,
        long companyId,
        string salesInvoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an e-archive invoice for a sales invoice that has already been created in Paraşüt.
    /// Used for customers who are NOT registered e-invoice payers.
    /// POST /v4/{company_id}/e_archives
    /// </summary>
    Task<JsonApiResponse<ParasutEInvoiceAttributes>> CreateEArchiveAsync(
        string accessToken,
        long companyId,
        string salesInvoiceId,
        CancellationToken cancellationToken = default);

}
