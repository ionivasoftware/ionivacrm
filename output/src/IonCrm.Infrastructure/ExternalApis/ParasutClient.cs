using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for Paraşüt accounting API v4 (https://api.parasut.com/v4/).
/// Uses the JSON:API specification for all request and response bodies.
///
/// Authentication: OAuth 2.0 password grant.
///   - Call <see cref="GetTokenAsync"/> once per connection to obtain tokens.
///   - Call <see cref="RefreshTokenAsync"/> before the access token expires (every ~2 hours).
///
/// Retry policy (Polly v8):
///   • 3 retries after initial attempt (4 total)
///   • Exponential backoff: 2 s → 4 s → 8 s (± jitter)
///   • Handles <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>
/// </summary>
public sealed class ParasutClient : IParasutClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParasutClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Initialises a new instance of <see cref="ParasutClient"/>.</summary>
    public ParasutClient(HttpClient httpClient, ILogger<ParasutClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Paraşüt API retry #{Attempt} in {Delay:0.##}s. Error: {Error}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    // ── OAuth ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ParasutTokenResponse> GetTokenAsync(
        ParasutTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: requesting access token for user {Username}.", request.Username);

        return await _retryPipeline.ExecuteAsync<ParasutTokenResponse>(async ct =>
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"]    = request.GrantType,
                ["client_id"]     = request.ClientId,
                ["client_secret"] = request.ClientSecret,
                ["username"]      = request.Username,
                ["password"]      = request.Password,
                ["redirect_uri"]  = request.RedirectUri
            };

            var response = await _httpClient.PostAsync(
                "https://api.parasut.com/oauth/token",
                new FormUrlEncodedContent(form),
                ct);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ParasutTokenResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty token response from Paraşüt.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParasutTokenResponse> RefreshTokenAsync(
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: refreshing access token.");

        return await _retryPipeline.ExecuteAsync<ParasutTokenResponse>(async ct =>
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["client_id"]     = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken,
                ["redirect_uri"]  = "urn:ietf:wg:oauth:2.0:oob"
            };

            var response = await _httpClient.PostAsync(
                "https://api.parasut.com/oauth/token",
                new FormUrlEncodedContent(form),
                ct);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ParasutTokenResponse>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty refresh response from Paraşüt.");
        }, cancellationToken);
    }

    // ── Contacts ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutContactAttributes>> GetContactsAsync(
        string accessToken, long companyId, int page = 1, int pageSize = 25,
        string? search = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching contacts. Company={CompanyId} Page={Page} Search={Search}", companyId, page, search);
        var url = $"v4/{companyId}/contacts?page[size]={pageSize}&page[number]={page}";
        // Note: Paraşüt v4 does not support server-side name filtering.
        // When a search term is present the handler fetches a large batch and filters in memory.

        return await GetListAsync<ParasutContactAttributes>(accessToken, url, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutContactAttributes>> GetContactByIdAsync(
        string accessToken, long companyId, string contactId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching contact {ContactId}. Company={CompanyId}", contactId, companyId);
        var url = $"v4/{companyId}/contacts/{contactId}";

        return await GetSingleAsync<ParasutContactAttributes>(accessToken, url, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutContactAttributes>> CreateContactAsync(
        string accessToken, long companyId, ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: creating contact '{Name}'. Company={CompanyId}", attributes.Name, companyId);
        var url = $"v4/{companyId}/contacts";
        var body = new JsonApiRequest<ParasutContactAttributes>(
            new JsonApiDataObject<ParasutContactAttributes>(null, "contacts", attributes));

        return await PostAsync<ParasutContactAttributes>(accessToken, url, body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutContactAttributes>> UpdateContactAsync(
        string accessToken, long companyId, string contactId, ParasutContactAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: updating contact {ContactId}. Company={CompanyId}", contactId, companyId);
        var url = $"v4/{companyId}/contacts/{contactId}";
        var body = new JsonApiRequest<ParasutContactAttributes>(
            new JsonApiDataObject<ParasutContactAttributes>(contactId, "contacts", attributes));

        return await PatchAsync<ParasutContactAttributes>(accessToken, url, body, cancellationToken);
    }

    // ── Sales Invoices ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutSalesInvoiceAttributes>> GetSalesInvoicesAsync(
        string accessToken, long companyId, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching invoices. Company={CompanyId} Page={Page}", companyId, page);
        var url = $"v4/{companyId}/sales_invoices?page[size]={pageSize}&page[number]={page}";

        return await GetListAsync<ParasutSalesInvoiceAttributes>(accessToken, url, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutSalesInvoiceAttributes>> GetSalesInvoiceByIdAsync(
        string accessToken, long companyId, string invoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching invoice {InvoiceId}. Company={CompanyId}", invoiceId, companyId);
        var url = $"v4/{companyId}/sales_invoices/{invoiceId}";

        return await GetSingleAsync<ParasutSalesInvoiceAttributes>(accessToken, url, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutSalesInvoiceAttributes>> CreateSalesInvoiceAsync(
        string accessToken, long companyId, CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: creating sales invoice. Company={CompanyId}", companyId);
        var url = $"v4/{companyId}/sales_invoices";

        return await _retryPipeline.ExecuteAsync<JsonApiResponse<ParasutSalesInvoiceAttributes>>(async ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(request, options: JsonOpts);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<ParasutSalesInvoiceAttributes>>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty response from Paraşüt.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutPaymentAttributes>> PaySalesInvoiceAsync(
        string accessToken, long companyId, string invoiceId, CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: recording payment for invoice {InvoiceId}. Company={CompanyId}", invoiceId, companyId);
        var url = $"v4/{companyId}/sales_invoices/{invoiceId}/payments";

        return await _retryPipeline.ExecuteAsync<JsonApiResponse<ParasutPaymentAttributes>>(async ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(request, options: JsonOpts);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<ParasutPaymentAttributes>>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty response from Paraşüt.");
        }, cancellationToken);
    }

    // ── Products ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutProductAttributes>> GetProductsAsync(
        string accessToken, long companyId, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching products. Company={CompanyId}", companyId);
        var url = $"v4/{companyId}/products?page[size]={pageSize}&page[number]={page}";

        return await GetListAsync<ParasutProductAttributes>(accessToken, url, cancellationToken);
    }

    // ── Accounts ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutAccountAttributes>> GetAccountsAsync(
        string accessToken, long companyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching accounts. Company={CompanyId}", companyId);
        var url = $"v4/{companyId}/accounts";

        return await GetListAsync<ParasutAccountAttributes>(accessToken, url, cancellationToken);
    }

    // ── Contact Invoices ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutSalesInvoiceAttributes>> GetContactInvoicesAsync(
        string accessToken, long companyId, string contactId,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching invoices for contact {ContactId}. Company={CompanyId}", contactId, companyId);
        var url = $"v4/{companyId}/sales_invoices?filter[contact_id]={Uri.EscapeDataString(contactId)}&page[size]={pageSize}&page[number]={page}&sort=-issue_date";

        return await GetListAsync<ParasutSalesInvoiceAttributes>(accessToken, url, cancellationToken);
    }

    // ── E-Invoice Inbox ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutEInvoiceInboxAttributes>> GetEInvoiceInboxesAsync(
        string accessToken, long companyId, string vkn,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: querying e-invoice inboxes for VKN {Vkn}. Company={CompanyId}", vkn, companyId);
        var url = $"v4/{companyId}/e_invoice_inboxes?filter[vkn]={Uri.EscapeDataString(vkn)}";

        return await GetListAsync<ParasutEInvoiceInboxAttributes>(accessToken, url, cancellationToken);
    }

    // ── E-Invoice / E-Archive Officialize ───────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutEInvoiceAttributes>> CreateEInvoiceAsync(
        string accessToken, long companyId, string salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: creating e-invoice for sales invoice {SalesInvoiceId}. Company={CompanyId}", salesInvoiceId, companyId);
        var url = $"v4/{companyId}/e_invoices";

        var body = new CreateEDocumentRequest
        {
            Data = new CreateEDocumentData
            {
                Type = "e_invoices",
                Relationships = new EDocumentRelationships
                {
                    Invoice = new EDocumentInvoiceRelationship
                    {
                        Data = new EDocumentInvoiceRelationshipData { Id = salesInvoiceId }
                    }
                }
            }
        };

        return await _retryPipeline.ExecuteAsync<JsonApiResponse<ParasutEInvoiceAttributes>>(async ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(body, options: JsonOpts);
            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<ParasutEInvoiceAttributes>>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty e-invoice response from Paraşüt.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutEInvoiceAttributes>> CreateEArchiveAsync(
        string accessToken, long companyId, string salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: creating e-archive for sales invoice {SalesInvoiceId}. Company={CompanyId}", salesInvoiceId, companyId);
        var url = $"v4/{companyId}/e_archives";

        var body = new CreateEDocumentRequest
        {
            Data = new CreateEDocumentData
            {
                Type = "e_archives",
                Relationships = new EDocumentRelationships
                {
                    Invoice = new EDocumentInvoiceRelationship
                    {
                        Data = new EDocumentInvoiceRelationshipData { Id = salesInvoiceId }
                    }
                }
            }
        };

        return await _retryPipeline.ExecuteAsync<JsonApiResponse<ParasutEInvoiceAttributes>>(async ct =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(body, options: JsonOpts);
            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<ParasutEInvoiceAttributes>>(JsonOpts, ct);
            return result ?? throw new InvalidOperationException("Empty e-archive response from Paraşüt.");
        }, cancellationToken);
    }

    // ── Contact Transactions ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<JsonApiListResponse<ParasutTransactionAttributes>> GetContactTransactionsAsync(
        string accessToken, long companyId, string contactId,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching transactions for contact {ContactId}. Company={CompanyId}", contactId, companyId);
        var url = $"v4/{companyId}/contacts/{contactId}/contact_debit_credit_transactions?page[size]={pageSize}&page[number]={page}&sort=-date";

        return await GetListAsync<ParasutTransactionAttributes>(accessToken, url, cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<JsonApiListResponse<T>> GetListAsync<T>(
        string accessToken, string url, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync<JsonApiListResponse<T>>(async token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBearer(req, accessToken);
            var response = await _httpClient.SendAsync(req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiListResponse<T>>(JsonOpts, token);
            return result ?? new JsonApiListResponse<T>(new List<JsonApiDataObject<T>>(), null);
        }, ct);
    }

    private async Task<JsonApiResponse<T>> GetSingleAsync<T>(
        string accessToken, string url, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync<JsonApiResponse<T>>(async token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBearer(req, accessToken);
            var response = await _httpClient.SendAsync(req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<T>>(JsonOpts, token);
            return result ?? throw new InvalidOperationException("Empty response from Paraşüt.");
        }, ct);
    }

    private async Task<JsonApiResponse<T>> PostAsync<T>(
        string accessToken, string url, object body, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync<JsonApiResponse<T>>(async token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(body, options: JsonOpts);
            var response = await _httpClient.SendAsync(req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<T>>(JsonOpts, token);
            return result ?? throw new InvalidOperationException("Empty response from Paraşüt.");
        }, ct);
    }

    private async Task<JsonApiResponse<T>> PatchAsync<T>(
        string accessToken, string url, object body, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync<JsonApiResponse<T>>(async token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, url);
            ApplyBearer(req, accessToken);
            req.Content = JsonContent.Create(body, options: JsonOpts);
            var response = await _httpClient.SendAsync(req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonApiResponse<T>>(JsonOpts, token);
            return result ?? throw new InvalidOperationException("Empty response from Paraşüt.");
        }, ct);
    }

    private static void ApplyBearer(HttpRequestMessage request, string accessToken) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
