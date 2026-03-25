using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http.Json;
using System.Text.Json;

namespace IonCrm.Infrastructure.ExternalApis;

/// <summary>
/// HTTP client for SaaS B external system.
/// SaaS B uses different endpoint paths (/customers/list, /subscriptions/all, /orders/all)
/// and X-Api-Key header authentication (configured at registration time in DI).
///
/// Retry policy (Polly v8 ResiliencePipeline):
///   • 3 retries after the initial attempt (4 total)
///   • Exponential backoff: 2 s → 4 s → 8 s (± jitter)
///   • Handles <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>
///   • Each retry attempt is logged via the injected <see cref="ILogger{T}"/>
/// </summary>
public sealed class SaasBClient : ISaasBClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SaasBClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="SaasBClient"/>.
    /// The <paramref name="httpClient"/> is pre-configured by DI (base address + X-Api-Key header).
    /// </summary>
    public SaasBClient(HttpClient httpClient, ILogger<SaasBClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Build a per-instance Polly v8 pipeline so the OnRetry delegate can
        // capture the logger for structured retry logging.
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
                        "SaaS B HTTP retry #{Attempt} in {Delay:0.##}s. Error: {Error}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<SaasBCustomersResponse> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching customers.");
        return await _retryPipeline.ExecuteAsync<SaasBCustomersResponse>(async ct =>
        {
            var response = await _httpClient.GetAsync("customers/list", ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBCustomersResponse>(JsonOpts, ct);
            return result ?? new SaasBCustomersResponse(new List<SaasBCustomer>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasBSubscriptionsResponse> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching subscriptions.");
        return await _retryPipeline.ExecuteAsync<SaasBSubscriptionsResponse>(async ct =>
        {
            var response = await _httpClient.GetAsync("subscriptions/all", ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBSubscriptionsResponse>(JsonOpts, ct);
            return result ?? new SaasBSubscriptionsResponse(new List<SaasBSubscription>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaasBOrdersResponse> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SaaS B: fetching orders.");
        return await _retryPipeline.ExecuteAsync<SaasBOrdersResponse>(async ct =>
        {
            var response = await _httpClient.GetAsync("orders/all", ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SaasBOrdersResponse>(JsonOpts, ct);
            return result ?? new SaasBOrdersResponse(new List<SaasBOrder>(), 0);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyCallbackAsync(SaasBCallbackPayload payload, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "SaaS B: posting callback. Event={Event} Type={Type} Id={Id}",
            payload.Event, payload.Type, payload.Id);

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            var response = await _httpClient.PostAsJsonAsync("webhooks/crm", payload, JsonOpts, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }
}
