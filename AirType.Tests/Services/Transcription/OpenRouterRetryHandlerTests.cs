using System.Net;
using System.Net.Http;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class OpenRouterRetryHandlerTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_RetriesTransientStatusCodes(HttpStatusCode transientStatus)
    {
        using var handler = new CountingHandler(call => Task.FromResult(
            new HttpResponseMessage(call == 1 ? transientStatus : HttpStatusCode.OK)));
        using var client = CreateClient(handler);

        using HttpResponseMessage response = await client.GetAsync("https://example.test/retry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_RetriesHttpRequestException()
    {
        using var handler = new CountingHandler(call => call == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Transient network failure."))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = CreateClient(handler);

        using HttpResponseMessage response = await client.GetAsync("https://example.test/retry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryNonTransientStatusCode()
    {
        using var handler = new CountingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var client = CreateClient(handler);

        using HttpResponseMessage response = await client.GetAsync("https://example.test/no-retry");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_StopsAfterThreeRetries()
    {
        using var handler = new CountingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = CreateClient(handler);

        using HttpResponseMessage response = await client.GetAsync("https://example.test/exhausted");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryCancellation()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var handler = new CountingHandler(_ =>
            Task.FromCanceled<HttpResponseMessage>(canceled.Token));
        using var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("https://example.test/canceled"));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_DisposesResponseDiscardedBeforeRetry()
    {
        var discardedContent = new TrackingContent();
        using var handler = new CountingHandler(call => Task.FromResult(
            call == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = discardedContent }
                : new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = CreateClient(handler);

        using HttpResponseMessage response = await client.GetAsync("https://example.test/disposal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(discardedContent.IsDisposed);
    }

    private static HttpClient CreateClient(HttpMessageHandler innerHandler)
    {
        return new HttpClient(new OpenRouterRetryHandler(innerHandler, TimeSpan.Zero));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<int, Task<HttpResponseMessage>> _responseFactory;

        public CountingHandler(Func<int, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _responseFactory(CallCount);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
