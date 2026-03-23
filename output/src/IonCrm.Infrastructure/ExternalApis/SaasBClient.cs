using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for SaaS B external system.
/// SaaS B uses different endpoints (/customers/list, /subscriptions/all)
/// with X-Api-Key header authentication (configured at registration time).
/// HTTP retry with exponential backoff (3 attempts) is handled internally.
/// </summary>
public sealed class SaasBClient : ISaasBClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SaasBClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="SaasBClient"/>.
    /// The <paramref name="httpClient"/> is configured by DI (base address + X-Api-Key header).
    /// </summary>
    public SaasBClient(HttpClient httpClient, ILogger<SaasBClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SaasBCustomersResponse> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("customers/list", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBCustomersResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasBCustomersResponse(new List<SaasBCustomer>(), 0);
        }, "GetCustomers");
    }

    /// <inheritdoc />
    public async Task<SaasBSubscriptionsResponse> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("subscriptions/all", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBSubscriptionsResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasBSubscriptionsResponse(new List<SaasBSubscription>(), 0);
        }, "GetSubscriptions");
    }

    /// <inheritdoc />
    public async Task<SaasBOrdersResponse> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync("orders/all", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBOrdersResponse>(JsonOpts, cancellationToken);
            return result ?? new SaasBOrdersResponse(new List<SaasBOrder>(), 0);
        }, "GetOrders");
    }

    /// <inheritdoc />
    public async Task NotifyCallbackAsync(SaasBCallbackPayload payload, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.PostAsJsonAsync("webhooks/crm", payload, JsonOpts, cancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }, "NotifyCallback");
    }

    // ── Retry with exponential backoff ────────────────────────────────────────

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operationName)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s
                _logger.LogWarning(ex,
                    "SaaS B {Operation} attempt {Attempt}/{MaxAttempts} failed. Retrying in {Delay}s.",
                    operationName, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay);
            }
            catch (TaskCanceledException ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "SaaS B {Operation} attempt {Attempt}/{MaxAttempts} timed out. Retrying in {Delay}s.",
                    operationName, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Final attempt — let exception propagate to caller
        return await action();
    }
}
