using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Scoped implementation of <see cref="IParasutService"/>.
///
/// This service is the single entry point for all Paraşüt API calls made from
/// Application-layer command/query handlers.  It transparently handles:
///
///   1. Connection lookup — fetches the <see cref="ParasutConnection"/> for the project
///   2. Token lifecycle   — delegates to <see cref="ParasutTokenHelper"/> (refresh / re-auth)
///   3. API dispatch      — delegates to <see cref="IParasutClient"/> (typed HttpClient)
///
/// All public methods return a <c>(Data?, Error?)</c> tuple.
/// <c>Error != null</c> means the call failed; callers should propagate this as a
/// <c>Result.Failure(error)</c> rather than throwing.
/// </summary>
public sealed class ParasutService : IParasutService
{
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly IParasutClient _parasutClient;
    private readonly ILogger<ParasutService> _logger;

    /// <summary>Initialises a new instance of <see cref="ParasutService"/>.</summary>
    public ParasutService(
        IParasutConnectionRepository connectionRepository,
        IParasutClient parasutClient,
        ILogger<ParasutService> logger)
    {
        _connectionRepository = connectionRepository;
        _parasutClient = parasutClient;
        _logger = logger;
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(ParasutConnection? Connection, string? Error)> GetConnectionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // GetEffectiveConnectionAsync: project-specific first, falls back to global (ProjectId = null).
        var stored = await _connectionRepository.GetEffectiveConnectionAsync(projectId, cancellationToken);

        return await ParasutTokenHelper.EnsureValidTokenAsync(
            stored,
            _parasutClient,
            _connectionRepository,
            _logger,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsConnectedAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionRepository.GetEffectiveConnectionAsync(projectId, cancellationToken);
        return connection?.IsConnected ?? false;
    }

    // ── Contacts ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(JsonApiListResponse<ParasutContactAttributes>? Data, string? Error)> GetContactsAsync(
        Guid projectId, int page = 1, int pageSize = 25, string? search = null,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetContactsAsync(
                conn.AccessToken!, conn.CompanyId, page, pageSize, search, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetContacts failed for project {ProjectId}.", projectId);
            return (null, $"Cariler alınamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> GetContactByIdAsync(
        Guid projectId, string contactId,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetContactByIdAsync(
                conn.AccessToken!, conn.CompanyId, contactId, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetContactById failed for project {ProjectId} contact {ContactId}.",
                projectId, contactId);
            return (null, $"Cari bulunamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> CreateContactAsync(
        Guid projectId, ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.CreateContactAsync(
                conn.AccessToken!, conn.CompanyId, attributes, cancellationToken);
            _logger.LogInformation(
                "Paraşüt: contact '{Name}' created for project {ProjectId}. ParasutId={Id}",
                attributes.Name, projectId, data.Data.Id);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt CreateContact failed for project {ProjectId}.", projectId);
            return (null, $"Cari oluşturulamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutContactAttributes>? Data, string? Error)> UpdateContactAsync(
        Guid projectId, string contactId, ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.UpdateContactAsync(
                conn.AccessToken!, conn.CompanyId, contactId, attributes, cancellationToken);
            _logger.LogInformation(
                "Paraşüt: contact {ContactId} updated for project {ProjectId}.", contactId, projectId);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt UpdateContact failed for project {ProjectId} contact {ContactId}.",
                projectId, contactId);
            return (null, $"Cari güncellenemedi: {ex.Message}");
        }
    }

    // ── Sales Invoices ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(JsonApiListResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetSalesInvoicesAsync(
        Guid projectId, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetSalesInvoicesAsync(
                conn.AccessToken!, conn.CompanyId, page, pageSize, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetSalesInvoices failed for project {ProjectId}.", projectId);
            return (null, $"Faturalar alınamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetSalesInvoiceByIdAsync(
        Guid projectId, string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetSalesInvoiceByIdAsync(
                conn.AccessToken!, conn.CompanyId, invoiceId, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetSalesInvoiceById failed for project {ProjectId} invoice {InvoiceId}.",
                projectId, invoiceId);
            return (null, $"Fatura bulunamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> CreateSalesInvoiceAsync(
        Guid projectId, CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.CreateSalesInvoiceAsync(
                conn.AccessToken!, conn.CompanyId, request, cancellationToken);
            _logger.LogInformation(
                "Paraşüt: sales invoice created for project {ProjectId}. ParasutId={Id}",
                projectId, data.Data.Id);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt CreateSalesInvoice failed for project {ProjectId}.", projectId);
            return (null, $"Fatura oluşturulamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiResponse<ParasutPaymentAttributes>? Data, string? Error)> PaySalesInvoiceAsync(
        Guid projectId, string invoiceId, CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.PaySalesInvoiceAsync(
                conn.AccessToken!, conn.CompanyId, invoiceId, request, cancellationToken);
            _logger.LogInformation(
                "Paraşüt: payment recorded for invoice {InvoiceId} project {ProjectId}.",
                invoiceId, projectId);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt PaySalesInvoice failed for project {ProjectId} invoice {InvoiceId}.",
                projectId, invoiceId);
            return (null, $"Ödeme kaydedilemedi: {ex.Message}");
        }
    }

    // ── Contact Invoices ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(JsonApiListResponse<ParasutSalesInvoiceAttributes>? Data, string? Error)> GetContactInvoicesAsync(
        Guid projectId, string contactId, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetContactInvoicesAsync(
                conn.AccessToken!, conn.CompanyId, contactId, page, pageSize, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Paraşüt GetContactInvoices failed for project {ProjectId} contact {ContactId}.",
                projectId, contactId);
            return (null, $"Cari faturaları alınamadı: {ex.Message}");
        }
    }

    // ── Accounts & Products ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(JsonApiListResponse<ParasutAccountAttributes>? Data, string? Error)> GetAccountsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetAccountsAsync(
                conn.AccessToken!, conn.CompanyId, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetAccounts failed for project {ProjectId}.", projectId);
            return (null, $"Hesaplar alınamadı: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<(JsonApiListResponse<ParasutProductAttributes>? Data, string? Error)> GetProductsAsync(
        Guid projectId, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (conn, error) = await GetConnectionAsync(projectId, cancellationToken);
        if (conn is null) return (null, error);

        try
        {
            var data = await _parasutClient.GetProductsAsync(
                conn.AccessToken!, conn.CompanyId, page, pageSize, cancellationToken);
            return (data, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paraşüt GetProducts failed for project {ProjectId}.", projectId);
            return (null, $"Ürünler alınamadı: {ex.Message}");
        }
    }
}
