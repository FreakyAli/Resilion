using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// <c>AddStandardResilienceHandler</c> — future-plans #39.
/// </summary>
public class StandardResilienceHandlerTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    // ─── Retry on transient outcomes ────────────────────────────────────────────────────────
    //
    // The first test is also the proof that the TYPED strategies bound: the pipeline is a
    // Pipeline<HttpResponseMessage> and the retry predicate is result-based, so if the typed
    // strategy were skipped the first 500 would come straight back after one send.

    [Fact]
    public async Task Async_TransientStatusCode_RetriesUntilSuccess()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Async_TransientStatusCodes_AreRetried(HttpStatusCode status)
    {
        var stub = StubHttpMessageHandler.WithStatuses(status, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Async_NonTransientStatusCode_DoesNotRetry(HttpStatusCode status)
    {
        var stub = StubHttpMessageHandler.WithStatuses(status);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, stub.Sends);
    }

    [Fact]
    public async Task Async_HttpRequestException_IsRetried()
    {
        var stub = new StubHttpMessageHandler((_, attempt, _) => attempt < 2
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Sends);
    }

    [Fact]
    public async Task Async_RetriesExhausted_ReturnsLastResponse()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        // The last response is returned rather than thrown: a 500 is a response, not a fault.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(4, stub.Sends); // 1 initial + 3 retries
    }

    // ─── Cancellation is never transient ────────────────────────────────────────────────────

    [Fact]
    public async Task Async_CallerCancellation_IsNotRetriedAndIsNotReportedAsTimeout()
    {
        var stub = new StubHttpMessageHandler((_, _, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }));
        using var _ = provider;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync(Endpoint, cts.Token));

        Assert.IsNotType<TimeoutRejectedException>(ex);
        Assert.True(stub.Sends <= 1);
    }

    // ─── The null-ShouldHandle substitution ─────────────────────────────────────────────────

    [Fact]
    public async Task Async_ReplacingRetryOptionsWholesale_KeepsTransientDefault()
    {
        // The trap this guards: a user replaces the record to change one number, and without the
        // substitution silently loses 5xx handling entirely.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
            }));
        using var _ = provider;

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public async Task Async_ExplicitShouldHandle_OverridesTransientDefault()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o => o.Retry = o.Retry with
            {
                Delay = RetryDelay.None,
                ShouldHandle = _ => false,
            }));
        using var _ = provider;

        await client.GetAsync(Endpoint);

        Assert.Equal(1, stub.Sends);
    }

    // ─── Timeouts ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_AttemptTimeout_ThrowsTimeoutRejectedAfterRetrying()
    {
        var fakeTime = new FakeTimeProvider();
        var entered = new SemaphoreSlim(0);

        var stub = new StubHttpMessageHandler(async (_, _, ct) =>
        {
            entered.Release();
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) };
                o.Retry = o.Retry with { MaxRetryAttempts = 1, Delay = RetryDelay.None };
            }),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var _ = provider;

        var request = client.GetAsync(Endpoint);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(5));
            fakeTime.Advance(TimeSpan.FromSeconds(2));
        }

        var ex = await Assert.ThrowsAsync<TimeoutRejectedException>(() => request);

        Assert.Equal(TimeSpan.FromSeconds(1), ex.ConfiguredTimeout);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public void AttemptTimeoutLongerThanTotal_ThrowsWhenTheClientIsCreated()
    {
        // Options are validated inside the registry's lazy build, and that build is triggered when
        // IHttpClientFactory constructs the handler chain — i.e. at CreateClient, not at the first
        // request. Worth pinning: it means a misconfiguration fails at client creation, which is
        // early enough to surface during startup smoke tests rather than in traffic.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var ex = Assert.Throws<InvalidOperationException>(() => TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) };
                o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(30) };
            })));

        Assert.Contains("can never fire", ex.Message);
    }

    // ─── Circuit breaker ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_CircuitBreaker_OpensThenRejectsWithoutSending()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.Retry = o.Retry with { MaxRetryAttempts = 0 };
                o.CircuitBreaker = o.CircuitBreaker with
                {
                    FailureRatioThreshold = 0.5,
                    MinimumThroughput = 2,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30),
                };
            }));
        using var _ = provider;

        for (var i = 0; i < 2; i++)
        {
            await client.GetAsync(Endpoint);
        }

        var sendsBeforeOpen = stub.Sends;

        await Assert.ThrowsAsync<CircuitBrokenException>(() => client.GetAsync(Endpoint));

        // Rejected without touching the network, which is the whole point.
        Assert.Equal(sendsBeforeOpen, stub.Sends);
    }

    // ─── Configuration and lifetime ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_ConfigureDelegate_RunsOnceAcrossManyRequests()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var invocations = 0;

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                Interlocked.Increment(ref invocations);
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }));
        using var _ = provider;

        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync(Endpoint);
        }

        // The pipeline is cached, so options are materialised once — but note the handler resolves
        // RequestReplay separately, so this asserts "small constant", not literally one.
        Assert.True(invocations <= 2, $"configure ran {invocations} times");
    }

    [Fact]
    public async Task Async_TimeProviderFromContainer_IsUsedByStrategies()
    {
        var fakeTime = new FakeTimeProvider();
        var entered = new SemaphoreSlim(0);

        var stub = new StubHttpMessageHandler(async (_, _, ct) =>
        {
            entered.Release();
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) };
                o.Retry = o.Retry with { MaxRetryAttempts = 0 };
            }),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var _ = provider;

        var request = client.GetAsync(Endpoint);
        await entered.WaitAsync(TimeSpan.FromSeconds(5));

        // Nothing happens until the fake clock moves, which proves the strategies use it.
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => request);
    }

    [Fact]
    public void Registration_TwiceOnTheSameClient_ThrowsAtRegistrationTime()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(TestClientFactory.ClientName);

        builder.AddStandardResilienceHandler();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddStandardResilienceHandler());

        Assert.Contains(TestClientFactory.ClientName, ex.Message);
    }

    [Fact]
    public async Task Async_TwoClients_HaveIndependentPipelines()
    {
        var failing = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);
        var healthy = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var services = new ServiceCollection();
        services.AddHttpClient("failing")
            .ConfigurePrimaryHttpMessageHandler(() => failing)
            .AddStandardResilienceHandler(o =>
            {
                o.Retry = o.Retry with { MaxRetryAttempts = 0 };
                o.CircuitBreaker = o.CircuitBreaker with
                {
                    FailureRatioThreshold = 0.5, MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromSeconds(30),
                };
            });
        services.AddHttpClient("healthy")
            .ConfigurePrimaryHttpMessageHandler(() => healthy)
            .AddStandardResilienceHandler(o => o.Retry = o.Retry with { MaxRetryAttempts = 0 });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var failingClient = factory.CreateClient("failing");
        for (var i = 0; i < 2; i++)
        {
            await failingClient.GetAsync(Endpoint);
        }

        await Assert.ThrowsAsync<CircuitBrokenException>(() => failingClient.GetAsync(Endpoint));

        // The other client's breaker is untouched.
        var response = await factory.CreateClient("healthy").GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Sync_Send_ThrowsNotSupported()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var (provider, client) = TestClientFactory.Build(stub, b => b.AddStandardResilienceHandler());
        using var _ = provider;

        // Must throw rather than silently bypassing the pipeline via DelegatingHandler.Send.
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        var ex = Assert.Throws<NotSupportedException>(() => client.Send(request));

        Assert.Contains("SendAsync", ex.Message);
        Assert.Equal(0, stub.Sends);
    }
}
