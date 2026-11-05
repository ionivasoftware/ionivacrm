using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Parasut;
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
/// Authentication — two flows side by side until Paraşüt retires the password grant (~05.12.2026):
///   • NEW: authorization code + PKCE on Keycloak (<see cref="BuildAuthorizationUrl"/>, <see cref="ExchangeCodeAsync"/>,
///     <see cref="RefreshOAuth2TokenAsync"/>, <see cref="RevokeOAuth2TokenAsync"/>). Token calls are made ONCE, without the
///     retry pipeline: refresh tokens rotate, so a blind retry would replay a token that is already dead.
///   • LEGACY: password grant (<see cref="GetTokenAsync"/>) + <see cref="RefreshTokenAsync"/> on api.parasut.com/oauth/token.
/// When <c>Parasut:OAuth:Environment=Staging</c> every REST call goes to the staging host as well (staging knows no prod data).
///
/// Retry policy (Polly v8):
///   • 3 retries after initial attempt (4 total)
///   • Exponential backoff: 2 s → 4 s → 8 s (± jitter)
///   • Handles <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>
/// </summary>
public sealed class ParasutClient : IParasutClient
{
    private readonly HttpClient _httpClient;
    private readonly ParasutOAuthSettings _oauth;
    private readonly ILogger<ParasutClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Initialises a new instance of <see cref="ParasutClient"/>.</summary>
    public ParasutClient(HttpClient httpClient, ParasutOAuthSettings oauth, ILogger<ParasutClient> logger)
    {
        _httpClient = httpClient;
        _oauth = oauth;
        _logger = logger;

        // Environment decides identity AND REST base together; the typed client is created per resolution, so this is safe.
        if (oauth.IsStaging) _httpClient.BaseAddress = new Uri(oauth.ApiBaseUrl + "/");

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

    // ── OAuth 2.0 authorization code + PKCE (Keycloak) ────────────────────────

    /// <inheritdoc />
    public bool OAuth2Available => _oauth.Available;

    /// <inheritdoc />
    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        EnsureOAuth2Configured();
        var query = new Dictionary<string, string>
        {
            ["client_id"]             = _oauth.ClientId!,
            ["redirect_uri"]          = _oauth.RedirectUri!,
            ["response_type"]         = "code",
            // The only scope Paraşüt documents.
            ["scope"]                 = "openid",
            ["state"]                 = state,
            // PKCE is mandatory and only S256 is accepted (on top of client_secret for a confidential client).
            ["code_challenge"]        = codeChallenge,
            ["code_challenge_method"] = "S256",
            // Skips Keycloak's identity-provider chooser and opens the Paraşüt login directly.
            ["kc_idp_hint"]           = "parasut",
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{_oauth.AuthorizationEndpoint}?{qs}";
    }

    /// <inheritdoc />
    public Task<ParasutTokenResponse> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        EnsureOAuth2Configured();
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["client_id"]     = _oauth.ClientId!,
            ["code"]          = code,
            ["redirect_uri"]  = _oauth.RedirectUri!,
            ["code_verifier"] = codeVerifier,
        };
        AddClientSecret(form);
        return PostOAuth2TokenAsync(form, "code exchange", cancellationToken);
    }

    /// <inheritdoc />
    public Task<ParasutTokenResponse> RefreshOAuth2TokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        EnsureOAuth2Configured();
        // No redirect_uri here: the legacy "urn:ietf:wg:oauth:2.0:oob" is not a registered address at Keycloak.
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = _oauth.ClientId!,
            ["refresh_token"] = refreshToken,
        };
        AddClientSecret(form);
        return PostOAuth2TokenAsync(form, "refresh", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeOAuth2TokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (!_oauth.Available || string.IsNullOrWhiteSpace(refreshToken)) return false;
        var form = new Dictionary<string, string>
        {
            ["client_id"]       = _oauth.ClientId!,
            ["token"]           = refreshToken,
            ["token_type_hint"] = "refresh_token",
        };
        AddClientSecret(form);
        try
        {
            using var content = new FormUrlEncodedContent(form);
            var response = await _httpClient.PostAsync(_oauth.RevocationEndpoint, content, cancellationToken);
            if (response.IsSuccessStatusCode) return true;
            _logger.LogWarning("Paraşüt belirteç iptali reddedildi: {Status}", (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Best effort: the local connection is cut either way; the firm can also stop access from Paraşüt.
            _logger.LogWarning(ex, "Paraşüt belirteç iptali başarısız.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ParasutMeResponse> GetMeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // Paraşüt v4: the user lives under /v4/me; companies come through the relationship + included block.
        var req = new HttpRequestMessage(HttpMethod.Get, "v4/me?include=companies");
        ApplyBearer(req, accessToken);
        var response = await _httpClient.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var ids = new List<long>();
        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("relationships", out var rels) &&
            rels.TryGetProperty("companies", out var companiesRel) &&
            companiesRel.TryGetProperty("data", out var companiesData) &&
            companiesData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in companiesData.EnumerateArray())
                if (item.TryGetProperty("id", out var idEl) && long.TryParse(idEl.GetString(), out var id)) ids.Add(id);
        }

        var names = new Dictionary<long, string?>();
        if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in included.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "companies") continue;
                if (!item.TryGetProperty("id", out var idEl) || !long.TryParse(idEl.GetString(), out var id)) continue;
                string? name = null;
                if (item.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("name", out var nameEl)) name = nameEl.GetString();
                names[id] = name;
                if (!ids.Contains(id)) ids.Add(id);
            }
        }

        return new ParasutMeResponse(ids.Select(id => new ParasutMeCompany(id, names.GetValueOrDefault(id))).ToList());
    }

    private void AddClientSecret(Dictionary<string, string> form)
    {
        // Public client: no client_secret field at all — an empty value breaks the request.
        if (_oauth.ClientSecret is { } secret) form["client_secret"] = secret;
    }

    private void EnsureOAuth2Configured()
    {
        if (!_oauth.Available)
            throw new InvalidOperationException("Paraşüt'ün yeni bağlantı yöntemi sunucuda yapılandırılmamış (Parasut__OAuth__ClientId ve Parasut__OAuth__RedirectUri).");
    }

    /// <summary>Single attempt against Keycloak's token endpoint. 400/401/403 → <see cref="ParasutTokenRejectedException"/> (dead grant); anything else propagates as transient.</summary>
    private async Task<ParasutTokenResponse> PostOAuth2TokenAsync(Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Paraşüt OAuth2 {Operation}.", operation);
        using var content = new FormUrlEncodedContent(form);
        var response = await _httpClient.PostAsync(_oauth.TokenEndpoint, content, cancellationToken);
        if ((int)response.StatusCode is 400 or 401 or 403)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ParasutTokenRejectedException((int)response.StatusCode, $"Paraşüt {operation} reddedildi ({(int)response.StatusCode}): {Truncate(body)}");
        }
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ParasutTokenResponse>(JsonOpts, cancellationToken);
        if (result is null || string.IsNullOrWhiteSpace(result.AccessToken))
            throw new InvalidOperationException($"Empty token response from Paraşüt ({operation}).");
        return result;
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] : value;

    // ── OAuth (legacy password grant — until cut-over) ────────────────────────

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

            // Log request JSON for debugging
            var reqJson = await req.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Paraşüt create invoice request body: {Body}", reqJson);

            var response = await _httpClient.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Paraşüt invoice creation failed {Status}. Response: {Body}",
                    (int)response.StatusCode, body);
                throw new HttpRequestException(
                    $"Paraşüt {(int)response.StatusCode}: {body}",
                    null,
                    response.StatusCode);
            }
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

    /// <inheritdoc />
    public async Task<JsonApiResponse<ParasutProductAttributes>> GetProductByIdAsync(
        string accessToken, long companyId, string productId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Paraşüt: fetching product {ProductId}. Company={CompanyId}", productId, companyId);
        var url = $"v4/{companyId}/products/{productId}";

        return await GetSingleAsync<ParasutProductAttributes>(accessToken, url, cancellationToken);
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
