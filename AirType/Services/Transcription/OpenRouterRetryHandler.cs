using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace AirType.Services.Transcription;

internal sealed class OpenRouterRetryHandler : DelegatingHandler
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    internal OpenRouterRetryHandler(HttpMessageHandler innerHandler, TimeSpan? retryDelay = null)
    {
        InnerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));

        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(IsTransientResponse),
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = retryDelay ?? DefaultRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                OnRetry = arguments =>
                {
                    string outcome = arguments.Outcome.Result is { } response
                        ? $"HTTP {(int)response.StatusCode} ({response.StatusCode})"
                        : arguments.Outcome.Exception?.GetType().Name ?? "unknown outcome";
                    Logger.Debug(
                        "OpenRouterClient",
                        $"Retry {arguments.AttemptNumber + 1} after {arguments.RetryDelay.TotalSeconds}s - {outcome}");
                    return default;
                }
            })
            .Build();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _retryPipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token).ConfigureAwait(false),
            cancellationToken).AsTask();
    }

    private static bool IsTransientResponse(HttpResponseMessage response)
    {
        return response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
    }
}
