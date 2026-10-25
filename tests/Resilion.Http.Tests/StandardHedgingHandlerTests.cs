using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// <c>AddStandardHedgingHandler</c> — the standard shape with hedging in place of retry.
/// </summary>
/// <remarks>
/// Loser disposal is covered by <see cref="ResponseDisposalTests"/> and per-attempt request
/// cloning by <see cref="RequestReplayTests"/>; this suite covers the delay modes, composition,
/// and registration.
/// </remarks>
public class StandardHedgingHandlerTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
        => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Endpoint),
            HttpCompletionOption.ResponseHeadersRead);

    // ─── Latency mode: hedge only if the primary is slow ────────────────────────────────────

    [Fact]
    public async Task Async_PrimaryAnswersBeforeTheDelay_NoHedgeIsLaunched()
    {
        // The point of latency mode: a fast primary costs nothing extra.
        var fakeTime = new FakeTimeProvider();
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.FromSeconds(2),
            }),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, stub.Sends);
    }

    [Fact]
    public async Task Async_PrimarySlow_HedgedAttemptWinsAfterTheDelay()
    {
        var fakeTime = new FakeTimeProvider();
        var primaryEntered = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, attempt, ct) =>
        {
            if (attempt == 0)
            {
                primaryEntered.SetResult();
                await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.FromSeconds(2),
            }),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var scope = provider;

        var request = SendAsync(client);
        await primaryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Nothing has hedged yet; advancing past the delay is what launches the second attempt.
        Assert.Equal(1, stub.Sends);
        fakeTime.Advance(TimeSpan.FromSeconds(3));

        using var response = await request;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    // ─── Parallel mode ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_ParallelMode_LaunchesEveryAttemptImmediately()
    {
        var launched = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, _, _) =>
        {
            launched.Release();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero,
            }));
        using var scope = provider;

        var request = SendAsync(client);

        // All three must be in flight before any of them answers.
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await launched.WaitAsync(TimeSpan.FromSeconds(10)), $"attempt {i} never launched");
        }

        release.SetResult();
        using var response = await request;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Sends);
    }

    // ─── Sequential mode ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_SequentialMode_LaunchesTheNextAttemptOnlyAfterTheLastFailed()
    {
        var inFlight = 0;
        var maxConcurrent = 0;

        var stub = new StubHttpMessageHandler(async (_, attempt, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxConcurrent, now);
            await Task.Yield();
            Interlocked.Decrement(ref inFlight);

            return new HttpResponseMessage(
                attempt < 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = o.Hedging with
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Sends);
        Assert.Equal(1, maxConcurrent);

        static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)))
            {
                if (Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    // ─── Predicate defaults and composition ────────────────────────────────────────────────

    [Fact]
    public async Task Async_ReplacingHedgingOptionsWholesale_KeepsTransientDefault()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o => o.Hedging = new HedgingStrategyOptions<HttpResponseMessage>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));
        using var scope = provider;

        using var response = await SendAsync(client);

        // Without the null-ShouldHandle substitution the 500 would be treated as success.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public async Task Async_CircuitBreakerStillAppliesUnderHedging()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o =>
            {
                o.Hedging = o.Hedging with
                {
                    MaxHedgedAttempts = 1,
                    HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                };
                o.CircuitBreaker = o.CircuitBreaker with
                {
                    FailureRatioThreshold = 0.5,
                    MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromSeconds(30),
                };
            }));
        using var scope = provider;

        for (var i = 0; i < 2; i++)
        {
            (await SendAsync(client)).Dispose();
        }

        var before = stub.Sends;
        await Assert.ThrowsAsync<CircuitBrokenException>(() => SendAsync(client));
        Assert.Equal(before, stub.Sends);
    }

    // ─── Registration and unsupported paths ────────────────────────────────────────────────

    [Fact]
    public void AttemptTimeoutLongerThanTotal_ThrowsWhenTheClientIsCreated()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var ex = Assert.Throws<InvalidOperationException>(() => TestClientFactory.Build(stub, b =>
            b.AddStandardHedgingHandler(o =>
            {
                o.TotalRequestTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) };
                o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(30) };
            })));

        Assert.Contains("can never fire", ex.Message);
    }

    [Fact]
    public void Registration_TwiceOnTheSameClient_ThrowsAtRegistrationTime()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(TestClientFactory.ClientName);

        builder.AddStandardHedgingHandler();

        Assert.Throws<InvalidOperationException>(() => builder.AddStandardHedgingHandler());
    }

    [Fact]
    public void StandardAndHedgingOnTheSameClient_BothRegister()
    {
        // Different kinds, so different pipeline keys — this must NOT be rejected.
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(TestClientFactory.ClientName);

        builder.AddStandardResilienceHandler();
        builder.AddStandardHedgingHandler();

        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    [Fact]
    public void Sync_Send_ThrowsNotSupported()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var (provider, client) = TestClientFactory.Build(stub, b => b.AddStandardHedgingHandler());
        using var scope = provider;

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        Assert.Throws<NotSupportedException>(() => client.Send(request));
        Assert.Equal(0, stub.Sends);
    }
}
