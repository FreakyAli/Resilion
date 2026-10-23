using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// <see cref="HttpRequestReplay"/> — whether each attempt sends the caller's request or a copy.
/// </summary>
/// <remarks>
/// This is the difference between a retry that works and one that throws on the second attempt:
/// non-replayable content cannot be sent twice, and an inner handler that adds rather than sets a
/// header will reject a reused message.
/// </remarks>
public class RequestReplayTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    private static HttpRequestMessage PostWith(HttpContent content)
        => new(HttpMethod.Post, Endpoint) { Content = content };

    // ─── ReuseRequest: the same instance, no buffering ──────────────────────────────────────

    [Fact]
    public async Task Async_ReuseRequest_SendsTheSameInstanceOnEveryAttempt()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.ReuseRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(3, stub.Requests.Count);
        Assert.All(stub.Requests, r => Assert.Same(original, r));
    }

    [Fact]
    public async Task Async_ReuseRequest_DoesNotBufferTheCallersContent()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var content = new TrackingHttpContent("body");

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.ReuseRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var request = PostWith(content);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Nothing read the body, so the handler did not buffer it. That is the point of this mode:
        // zero overhead, at the cost of requiring replayable content.
        Assert.Equal(0, content.Serializations);
    }

    // ─── CloneRequest: independent copies, buffered exactly once ────────────────────────────

    [Fact]
    public async Task Async_CloneRequest_SendsADistinctInstanceOnEveryAttempt()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.CloneRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        var sent = stub.Requests;
        Assert.Equal(3, sent.Count);
        Assert.All(sent, r => Assert.NotSame(original, r));
        Assert.Equal(3, sent.Distinct().Count());
    }

    [Fact]
    public async Task Async_CloneRequest_BuffersTheCallersContentExactlyOnce()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var content = new TrackingHttpContent("body");

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.CloneRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var request = PostWith(content);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Once up front, not once per attempt: the clones share the captured byte array.
        Assert.Equal(3, stub.Sends);
        Assert.Equal(1, content.Serializations);
    }

    [Fact]
    public async Task Async_CloneRequest_EveryAttemptSeesTheSameBody()
    {
        var bodies = new List<string>();
        var stub = new StubHttpMessageHandler(async (request, attempt, _) =>
        {
            bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync());

            return new HttpResponseMessage(
                attempt < 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.CloneRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var request = PostWith(new StringContent("the-body"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(3, bodies.Count);
        Assert.All(bodies, b => Assert.Equal("the-body", b));
    }

    [Fact]
    public async Task Async_CloneRequest_CopiesMethodUriVersionHeadersAndOptions()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var optionKey = new HttpRequestOptionsKey<string>("test-option");

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.RequestReplay = HttpRequestReplay.CloneRequest));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        original.Version = new Version(2, 0);
        original.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        original.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc-123");
        original.Options.Set(optionKey, "option-value");
        original.Content!.Headers.TryAddWithoutValidation("X-Content-Marker", "marker");

        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        var sent = Assert.Single(stub.Requests);
        Assert.NotSame(original, sent);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(Endpoint, sent.RequestUri);
        Assert.Equal(new Version(2, 0), sent.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, sent.VersionPolicy);
        Assert.Equal("abc-123", sent.Headers.GetValues("X-Correlation-Id").Single());
        Assert.True(sent.Options.TryGetValue(optionKey, out var carried));
        Assert.Equal("option-value", carried);
        Assert.Equal("marker", sent.Content!.Headers.GetValues("X-Content-Marker").Single());
    }

    [Fact]
    public async Task Async_CloneRequest_LeavesTheCallersRequestUsable()
    {
        // The handler disposes its clones, never the caller's request — the caller owns that.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.RequestReplay = HttpRequestReplay.CloneRequest));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        // Still readable, so neither it nor its content was disposed underneath us.
        Assert.Equal("body", await original.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task Async_CloneRequest_WithNoContent_Works()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.CloneRequest;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var scope = provider;

        using var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
        Assert.All(stub.Requests, r => Assert.Null(r.Content));
    }

    // ─── Hedging always clones, whatever the caller asked for ───────────────────────────────

    [Fact]
    public async Task Async_Hedging_AlwaysGivesEachAttemptItsOwnRequest()
    {
        // Not configurable: concurrent attempts cannot share one HttpRequestMessage.
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        var sent = stub.Requests;
        Assert.Equal(2, sent.Count);
        Assert.All(sent, r => Assert.NotSame(original, r));
        Assert.Equal(2, sent.Distinct().Count());
    }

    // ─── The custom handler takes the mode explicitly ───────────────────────────────────────

    [Fact]
    public async Task Async_CustomHandler_HonoursTheRequestedReplayMode()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler(
                "custom",
                pb => pb.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    ShouldHandle = HttpResiliencePredicates.IsTransient,
                }),
                HttpRequestReplay.CloneRequest));
        using var scope = provider;

        using var original = PostWith(new StringContent("body"));
        using var response = await client.SendAsync(original, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
        Assert.All(stub.Requests, r => Assert.NotSame(original, r));
    }
}
