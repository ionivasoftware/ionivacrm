using System.Net.Http.Json;
using System.Text.Json;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for the Liftdesk (EMS) pricing management API. Base URL + Bearer key are passed per
/// call (they live on the Liftdesk <c>Project</c> row). Responses are normalised into the shared
/// <see cref="LiftdeskEnvelope{T}"/>: a parseable envelope is returned as-is (marked failed on non-2xx),
/// and transport/circuit-breaker failures become a legible Turkish failure envelope.
/// </summary>
public sealed class LiftdeskPricingClient : ILiftdeskPricingClient
{
    private const string PricingRoot = "/api/v1/crm/pricing";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LiftdeskPricingClient> _logger;

    /// <summary>Initialises a new instance of <see cref="LiftdeskPricingClient"/>.</summary>
    public LiftdeskPricingClient(HttpClient httpClient, ILogger<LiftdeskPricingClient> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<List<LiftdeskPricingPlan>>> GetPlansAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default)
        => SendAsync<List<LiftdeskPricingPlan>>(
            BuildRequest(HttpMethod.Get, baseUrl, apiKey, "/plans"), cancellationToken);

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<LiftdeskPricingPlan>> UpdatePlanAsync(
        string baseUrl, string apiKey, Guid id, UpdatePricingPlanRequest body, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Put, baseUrl, apiKey, $"/plans/{id}");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        return SendAsync<LiftdeskPricingPlan>(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<List<LiftdeskSmsPackage>>> GetSmsPackagesAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default)
        => SendAsync<List<LiftdeskSmsPackage>>(
            BuildRequest(HttpMethod.Get, baseUrl, apiKey, "/sms-packages"), cancellationToken);

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<LiftdeskSmsPackage>> CreateSmsPackageAsync(
        string baseUrl, string apiKey, CreateSmsPackageRequest body, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Post, baseUrl, apiKey, "/sms-packages");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        return SendAsync<LiftdeskSmsPackage>(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<LiftdeskSmsPackage>> UpdateSmsPackageAsync(
        string baseUrl, string apiKey, Guid id, UpdateSmsPackageRequest body, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(HttpMethod.Put, baseUrl, apiKey, $"/sms-packages/{id}");
        request.Content = JsonContent.Create(body, options: JsonOpts);
        return SendAsync<LiftdeskSmsPackage>(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LiftdeskEnvelope<object>> DeleteSmsPackageAsync(
        string baseUrl, string apiKey, Guid id, CancellationToken cancellationToken = default)
        => SendAsync<object>(
            BuildRequest(HttpMethod.Delete, baseUrl, apiKey, $"/sms-packages/{id}"), cancellationToken);

    private static HttpRequestMessage BuildRequest(HttpMethod method, string baseUrl, string apiKey, string relativePath)
    {
        var request = new HttpRequestMessage(method, $"{NormalizeBaseUrl(baseUrl)}{PricingRoot}{relativePath}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        return request;
    }

    /// <summary>Ensures the configured base URL has a scheme and no trailing slash, so
    /// <c>{base}/api/v1/crm/pricing/...</c> concatenation always yields a valid absolute URI.</summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }
        return trimmed.TrimEnd('/');
    }

    /// <summary>
    /// Sends the request and normalises the response into an envelope. A parseable envelope is returned
    /// as-is (forced to failed when the HTTP status is non-2xx); unparseable bodies and transport /
    /// broken-circuit failures become a synthetic failure with an operator-facing Turkish message.
    /// </summary>
    private async Task<LiftdeskEnvelope<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
        }
        catch (BrokenCircuitException)
        {
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk fiyat servisi geçici olarak devre dışı (art arda hata alındı, kısa süre sonra denenecek).",
                null, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout (60s) surfaces as TaskCanceledException/OperationCanceledException;
            // only the request's own token indicates a genuine caller cancellation.
            return new LiftdeskEnvelope<T>(false, default,
                "Liftdesk fiyat servisi zaman aşımına uğradı. Lütfen tekrar deneyin.", null, 504);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Liftdesk pricing: bağlantı hatası.");
            return new LiftdeskEnvelope<T>(false, default, $"Liftdesk fiyat servisine bağlanılamadı: {ex.Message}", null, 502);
        }

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var parseFailed = false;

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<LiftdeskEnvelope<T>>(rawBody, JsonOpts);
                if (envelope is not null)
                {
                    if (!response.IsSuccessStatusCode && envelope.Success)
                        return envelope with { Success = false, StatusCode = (int)response.StatusCode };
                    return envelope;
                }
            }
            catch (JsonException ex)
            {
                parseFailed = true;
                _logger.LogWarning(ex, "Liftdesk pricing: yanıt gövdesi çözümlenemedi. HTTP {Status}", (int)response.StatusCode);
            }
        }

        if (parseFailed)
            return new LiftdeskEnvelope<T>(false, default,
                $"Liftdesk fiyat yanıtı çözümlenemedi (HTTP {(int)response.StatusCode}).", null, (int)response.StatusCode);

        if (response.IsSuccessStatusCode)
            return new LiftdeskEnvelope<T>(true, default, null, null, (int)response.StatusCode);

        var message = (int)response.StatusCode switch
        {
            401 => "Liftdesk API anahtarı geçersiz veya eksik (401).",
            503 => "Liftdesk tarafında fiyat API anahtarı (LIFTDESKSAAS__APIKEY) tanımlı değil (503).",
            _   => $"Liftdesk fiyat servisi beklenmedik yanıt döndü: HTTP {(int)response.StatusCode}",
        };
        return new LiftdeskEnvelope<T>(false, default, message, null, (int)response.StatusCode);
    }
}
