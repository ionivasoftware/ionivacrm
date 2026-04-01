using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// High-level application service for the Paraşüt accounting integration.
///
/// Wraps connection management, OAuth token lifecycle, and all Paraşüt API calls
/// behind a single project-scoped interface.  Callers pass only a <paramref name="projectId"/>
/// and business arguments — this service resolves the stored <see cref="ParasutConnection"/>,
/// refreshes the access token if needed (using <c>ParasutTokenHelper</c> three-tier strategy),
/// and delegates the HTTP call to <see cref="IParasutClient"/>.
///
/// Scoped lifetime — one instance per HTTP request (aligns with <c>IParasutConnectionRepository</c>).
/// </summary>
public interface IParasutService
{
    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the stored <see cref="ParasutConnection"/> for the given project,
    /// ensuring the access token is valid before returning.
    /// Returns <c>null</c> (+ error message) when no connection exists or re-auth fails.
    /// </summary>
    Task<(ParasutConnection? Connection, string? Error)> GetConnectionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the project has a stored connection with a valid access token.
    /// Does NOT attempt to refresh — use <see cref="GetConnectionAsync"/> for that.
    /// </summary>
    Task<bool> IsConnectedAsync(Guid projectId, CancellationToken cancellationToken = default);

    // ── Contacts (Cariler) ────────────────────────────────────────────────────

    /// <summary>Returns a paginated list of contacts for the project's Paraşüt company.</summary>
    Task<(JsonApiListResponse<ParasutContactAttributes>? Data, string? Error)> GetContactsAsync(
        Guid projectId,
        int page = 1,
        int pageSize = 25,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single contact by its Paraşüt ID.</summary>
    Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> GetContactByIdAsync(
        Guid projectId,
        string contactId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new contact (cari) in the project's Paraşüt company.</summary>
    Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> CreateContactAsync(
        Guid projectId,
        ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing contact (cari) in the project's Paraşüt company.</summary>
    Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> UpdateContactAsync(
        Guid projectId,
        string contactId,
        ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default);

    // ── Sales Invoices (Satış Faturaları) ─────────────────────────────────────

    /// <summary>Returns a paginated list of sales invoices for the project's company.</summary>
    Task<(JsonApiListResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetSalesInvoicesAsync(
        Guid projectId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single sales invoice by its Paraşüt ID.</summary>
    Task<(JsonApiResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetSalesInvoiceByIdAsync(
        Guid projectId,
        string invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new sales invoice in the project's company.</summary>
    Task<(JsonApiResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> CreateSalesInvoiceAsync(
        Guid projectId,
        CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records a payment against a sales invoice.</summary>
    Task<(JsonApiResponse<ParasutPaymentAttributes>? Data, string? Error)> PaySalesInvoiceAsync(
        Guid projectId,
        string invoiceId,
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default);

    // ── Contact Invoices (Cari Hareketleri) ───────────────────────────────────

    /// <summary>Returns a paginated list of invoices filtered by a specific contact (cari).</summary>
    Task<(JsonApiListResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetContactInvoicesAsync(
        Guid projectId,
        string contactId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    // ── Accounts & Products ───────────────────────────────────────────────────

    /// <summary>Returns all bank accounts and cash registers for the project's company.</summary>
    Task<(JsonApiListResponse<ParasutAccountAttributes>? Data, string? Error)> GetAccountsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a paginated list of products/services for the project's company.</summary>
    Task<(JsonApiListResponse<ParasutProductAttributes>? Data, string? Error)> GetProductsAsync(
        Guid projectId,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);
}
