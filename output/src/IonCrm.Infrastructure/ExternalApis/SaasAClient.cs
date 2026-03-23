using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for SaaS A external system.
/// SaaS A uses standard REST endpoints under /api/v1/ with Bearer token authentication.
/// HTTP retry with exponential backoff (3 attempts) is handled internally.
/// </summary>
public sealed class SaasAClient : ISaasAClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SaasAClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="SaasAClient"/>.
    /// The <paramref name="httpClient"/> is configured by DI (base address + auth header).
    /// </summary>
    public SaasAClient(HttpClient httpClient, ILogger<SaasAClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SaasACustomersResponse> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("api/v1/customers", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasACustomersResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasACustomersResponse(new List<SaasACustomer>(), 0);
        }, "GetCustomers");
    }

    /// <inheritdoc />
    public async Task<SaasASubscriptionsResponse> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("api/v1/subscriptions", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasASubscriptionsResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasASubscriptionsResponse(new List<SaasASubscription>(), 0);
        }, "GetSubscriptions");
    }

    /// <inheritdoc />
    public async Task<SaasAOrdersResponse> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("api/v1/orders", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasAOrdersResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasAOrdersResponse(new List<SaasAOrder>(), 0);
        }, "GetOrders");
    }

    /// <inheritdoc />
    public async Task NotifyCallbackAsync(SaasACallbackPayload payload, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/crm-callbacks", payload, JsonOpts, cancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }, "NotifyCallback");
    }

    // ── Polly-powered retry with exponential backoff ──────────────────────────

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operationName)
    {
        const int maxAttempts = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s
                _logger.LogWarning(ex,
                    "SaaS A {Operation} attempt {Attempt}/{MaxAttempts} failed. Retrying in {Delay}s.",
                    operationName, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay);
            }
            catch (TaskCanceledException ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "SaaS A {Operation} attempt {Attempt}/{MaxAttempts} timed out. Retrying in {Delay}s.",
                    operationName, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Final attempt — let exception propagate
        return await action();
    }
}
