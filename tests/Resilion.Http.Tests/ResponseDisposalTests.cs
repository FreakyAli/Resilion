using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// Discarded responses must be disposed, or every retry leaks a connection.
/// </summary>
/// <remarks>
/// The pipeline abandons non-final outcomes without disposing them — a retry overwrites its
/// outcome each iteration, hedging returns the first success and drops the rest — so the handler
/// owns this. It cannot be done with <c>OnRetry</c>, because a user-supplied handler can only be
/// replaced, not wrapped.
/// <para>
/// All tests read with <see cref="HttpCompletionOption.ResponseHeadersRead"/> so
/// <see cref="HttpClient"/> does not buffer and dispose the content itself, which would make the
/// assertions meaningless.
/// </para>
/// </remarks>
public class ResponseDisposalTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    private static (StubHttpMessageHandler Stub, List<DisposeTrackingContent> Contents) TrackingStub(
        params HttpStatusCode[] statuses)
    {
        var contents = new List<DisposeTrackingContent>();
        var stub = new StubHttpMessageHandler((_, attempt, _) =>
        {
            var content = new DisposeTrackingContent();
            lock (contents) { contents.Add(content); }
            return Task.FromResult(new HttpResponseMessage(
                statuses[Math.Min(attempt, statuses.Length - 1)]) { Content = content });
        });

        return (stub, contents);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
        => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Endpoint),
            HttpCompletionOption.ResponseHeadersRead);

    // ─── Retry ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_Retry_DiscardedResponses_AreDisposed_AndTheWinnerIsNot()
    {
        var (stub, contents) = TrackingStub(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, contents.Count);
        Assert.True(contents[0].IsDisposed, "first 500 was leaked");
        Assert.True(contents[1].IsDisposed, "second 500 was leaked");
        Assert.False(contents[2].IsDisposed, "the returned response must not be disposed");
    }

    [Fact]
    public async Task Async_Retry_PreviousResponse_IsDisposedBeforeTheNextAttemptStarts()
    {
        // Eager disposal, not deferred-to-the-end: with three retries and exponential backoff,
        // "at the end" can be tens of seconds of a connection held for nothing.
        var contents = new List<DisposeTrackingContent>();
        var observations = new List<bool>();

        var stub = new StubHttpMessageHandler((_, attempt, _) =>
        {
            if (attempt > 0)
            {
                lock (contents) { observations.Add(contents[attempt - 1].IsDisposed); }
            }

            var content = new DisposeTrackingContent();
            lock (contents) { contents.Add(content); }
            return Task.FromResult(new HttpResponseMessage(
                attempt < 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = content });
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(2, observations.Count);
        Assert.All(observations, disposed =>
            Assert.True(disposed, "the previous attempt's response was still open"));
    }

    [Fact]
    public async Task Async_RetriesExhausted_AllButTheReturnedResponse_AreDisposed()
    {
        var (stub, contents) = TrackingStub(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var scope = provider;

        using var response = await SendAsync(client);

        // The final 500 is handed back to the caller, so only the earlier ones are disposed.
        Assert.Equal(4, contents.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(contents[i].IsDisposed, $"attempt {i} was leaked");
        }

        Assert.False(contents[3].IsDisposed);
    }

    [Fact]
    public async Task Async_TotalTimeoutThrows_ResponsesProducedBeforeIt_AreDisposed()
    {
        // When the pipeline throws there is no winner, so nothing may survive. Driven by the total
        // timeout firing during a retry delay, after earlier attempts have already produced
        // responses the handler is holding.
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var (stub, contents) = TrackingStub(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(5) };
                o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) };
                o.Retry = o.Retry with
                {
                    MaxRetryAttempts = 5,
                    Delay = RetryDelay.Constant(TimeSpan.FromSeconds(2)),
                    UseJitter = false,
                };
            }),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var scope = provider;

        var request = SendAsync(client);

        // Advance past the total timeout while the retry loop is sleeping between attempts.
        for (var i = 0; i < 4 && !request.IsCompleted; i++)
        {
            await Task.Delay(50);
            fakeTime.Advance(TimeSpan.FromSeconds(2));
        }

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => request);

        List<DisposeTrackingContent> seen;
        lock (contents) { seen = contents.ToList(); }

        Assert.NotEmpty(seen);
        Assert.All(seen, c => Assert.True(c.IsDisposed, "a response was leaked when the pipeline threw"));
    }

    // ─── Hedging: attempts overlap, so disposal cannot be eager ─────────────────────────────

    [Fact]
    public async Task Async_Hedging_LoserThatAnswered_IsDisposed()
    {
        // The loser must genuinely ANSWER, not be cancelled: a cancelled attempt produces no
        // response and so nothing to dispose, which makes such a test vacuous. Sequential mode
        // gives a deterministic ordering — attempt 0 answers 500, hedging treats that as a failure
        // and launches attempt 1, which answers 200 and wins. Attempt 0's response was tracked
        // before the handler returned, so the concurrent-cleanup path must dispose it.
        var (stub, contents) = TrackingStub(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);

        List<DisposeTrackingContent> seen;
        lock (contents) { seen = contents.ToList(); }

        Assert.Equal(2, seen.Count);
        Assert.True(seen[0].IsDisposed, "the losing hedged response was leaked");
        Assert.False(seen[1].IsDisposed, "the returned response must not be disposed");
    }

    [Fact]
    public async Task Async_Hedging_Parallel_LoserThatAnswered_IsDisposed()
    {
        // The genuinely parallel case, made deterministic without any Task.Delay.
        //
        // Both attempts launch at once (HedgingDelay = Zero) and each blocks on its own gate. The
        // test releases attempt 0, which answers a handled failure; hedging therefore keeps
        // waiting, and only then is attempt 1 released to answer the winning 200.
        //
        // The ordering is guaranteed rather than hoped for: the handler calls Track before its
        // attempt task completes, so hedging cannot have observed attempt 0's failure unless
        // attempt 0's response was already tracked. Both are therefore in the concurrent
        // collection before the handler returns and closes the tracker.
        var contents = new List<DisposeTrackingContent>();
        var gate0 = new TaskCompletionSource();
        var gate1 = new TaskCompletionSource();
        var answered0 = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, attempt, _) =>
        {
            if (attempt == 0)
            {
                await gate0.Task;
                var failure = new DisposeTrackingContent();
                lock (contents) { contents.Add(failure); }
                answered0.SetResult();
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = failure };
            }

            await gate1.Task;
            var success = new DisposeTrackingContent();
            lock (contents) { contents.Add(success); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = success };
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.Zero,
            }));
        using var scope = provider;

        var request = SendAsync(client);

        gate0.SetResult();
        await answered0.Task.WaitAsync(TimeSpan.FromSeconds(10));
        gate1.SetResult();

        using var response = await request;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<DisposeTrackingContent> seen;
        lock (contents) { seen = contents.ToList(); }

        Assert.Equal(2, seen.Count);
        Assert.Equal(1, seen.Count(c => !c.IsDisposed));
        Assert.True(seen[0].IsDisposed, "the losing parallel hedged response was leaked");
    }

    [Fact]
    public async Task Async_Hedging_MultipleLosersThatAnswered_AreAllDisposed()
    {
        // Exercises the cleanup loop over more than one entry: two losers, not one, so an
        // implementation that disposed only the most recent would fail here.
        var contents = new List<DisposeTrackingContent>();
        var gates = new[] { new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource() };
        var answered = new[] { new TaskCompletionSource(), new TaskCompletionSource() };

        var stub = new StubHttpMessageHandler(async (_, attempt, _) =>
        {
            await gates[attempt].Task;

            var content = new DisposeTrackingContent();
            lock (contents) { contents.Add(content); }

            if (attempt < 2)
            {
                answered[attempt].SetResult();
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = content };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero,
            }));
        using var scope = provider;

        var request = SendAsync(client);

        // Both failures answer first, in order, then the winner.
        for (var i = 0; i < 2; i++)
        {
            gates[i].SetResult();
            await answered[i].Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        gates[2].SetResult();

        using var response = await request;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<DisposeTrackingContent> seen;
        lock (contents) { seen = contents.ToList(); }

        Assert.Equal(3, seen.Count);
        Assert.True(seen[0].IsDisposed, "first losing response was leaked");
        Assert.True(seen[1].IsDisposed, "second losing response was leaked");
        Assert.Equal(1, seen.Count(c => !c.IsDisposed));
    }

    [Fact]
    public async Task Async_Hedging_StragglerCompletingAfterTheHandlerReturned_IsDisposed()
    {
        // The tracker's "closed" state: hedging cancels losers and waits only a bounded time
        // before returning, so a straggler can produce a response after we have already answered.
        var contents = new List<DisposeTrackingContent>();
        var release = new TaskCompletionSource();
        var straggled = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, attempt, _) =>
        {
            if (attempt == 0)
            {
                // Deliberately ignores cancellation, which is what makes it a straggler.
                await release.Task;
                var late = new DisposeTrackingContent();
                lock (contents) { contents.Add(late); }
                straggled.SetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = late };
            }

            var content = new DisposeTrackingContent();
            lock (contents) { contents.Add(content); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.Zero,
            }));
        using var scope = provider;

        using var response = await SendAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Now let the abandoned attempt produce its response, after the handler has returned.
        release.SetResult();
        await straggled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        var late = contents[^1];
        Assert.True(late.IsDisposed, "a response produced after completion was leaked");
    }
}
