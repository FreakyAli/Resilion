using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resilion.Extensions;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// <c>AddResilienceHandler</c>, and the <see cref="IServiceProvider"/> overloads across all three
/// handler kinds.
/// </summary>
public class CustomResilienceHandlerTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    private sealed class Marker
    {
        public string Value { get; init; } = "from-di";
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
        => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Endpoint),
            HttpCompletionOption.ResponseHeadersRead);

    // ─── A caller-built pipeline ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_CustomPipeline_AppliesTheCallersStrategies()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler("custom", pb => pb.AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 2,
                    Delay = RetryDelay.None,
                    // Result-only: an exception would not match, so a silently skipped typed
                    // strategy would return the first 503 after one send.
                    ShouldHandle = o => o.TryGetResult(out var r)
                        && r is not null
                        && r.StatusCode == HttpStatusCode.ServiceUnavailable,
                })));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Sends);
    }

    [Fact]
    public async Task Async_CustomPipeline_CanUsePredicateBuilder()
    {
        // The sugar from #43 reaching its intended audience.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler("custom", pb => pb.AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => (int)r.StatusCode >= 500),
                })));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public async Task Async_CustomPipeline_TimeProviderComesFromTheContainer()
    {
        var fakeTime = new FakeTimeProvider();
        var entered = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddResilienceHandler("custom", pb => pb.AddTimeout(TimeSpan.FromSeconds(1))),
            s => s.AddSingleton<TimeProvider>(fakeTime));
        using var scope = provider;

        var request = SendAsync(client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => request);
    }

    [Fact]
    public async Task Async_CustomPipeline_CanOverrideTheTimeProvider()
    {
        // ConfigureCustom pre-seeds from the container, but the caller still owns the builder.
        var containerTime = new FakeTimeProvider();
        var ownTime = new FakeTimeProvider();
        var entered = new TaskCompletionSource();

        var stub = new StubHttpMessageHandler(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(TimeSpan.FromMinutes(1), ownTime, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddResilienceHandler("custom", pb =>
            {
                pb.TimeProvider = ownTime;
                pb.AddTimeout(TimeSpan.FromSeconds(1));
            }),
            s => s.AddSingleton<TimeProvider>(containerTime));
        using var scope = provider;

        var request = SendAsync(client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Advancing the container's clock must do nothing; only the caller's clock drives it.
        containerTime.Advance(TimeSpan.FromSeconds(30));
        Assert.False(request.IsCompleted);

        ownTime.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => request);
    }

    // ─── The IServiceProvider overloads ─────────────────────────────────────────────────────

    [Fact]
    public async Task Async_CustomPipeline_ServiceProviderOverload_ResolvesSingletons()
    {
        var seen = string.Empty;
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddResilienceHandler("custom", (pb, sp) =>
            {
                seen = sp.GetRequiredService<Marker>().Value;
                pb.AddTimeout(TimeSpan.FromSeconds(30));
            }),
            s => s.AddSingleton(new Marker()));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("from-di", seen);
    }

    [Fact]
    public async Task Async_StandardHandler_ServiceProviderOverload_ResolvesSingletons()
    {
        var seen = string.Empty;
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler((o, sp) =>
            {
                seen = sp.GetRequiredService<Marker>().Value;
                o.Retry = o.Retry with { Delay = RetryDelay.None };
            }),
            s => s.AddSingleton(new Marker()));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal("from-di", seen);
    }

    [Fact]
    public async Task Async_HedgingHandler_ServiceProviderOverload_ResolvesSingletons()
    {
        var seen = string.Empty;
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardHedgingHandler((o, sp) =>
            {
                seen = sp.GetRequiredService<Marker>().Value;
                o.Hedging = o.Hedging with
                {
                    MaxHedgedAttempts = 1,
                    HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                };
            }),
            s => s.AddSingleton(new Marker()));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal("from-di", seen);
    }

    [Fact]
    public async Task Async_CircuitBreakerState_SurvivesHandlerRotation()
    {
        // The invariant behind building the pipeline in the registry rather than in the handler
        // factory. That factory re-runs on every IHttpClientFactory rotation, so a pipeline built
        // there would reset the breaker on each one — a breaker that can never trip, with no
        // symptom a user could attribute to this library.
        //
        // Deliberately slow: SetHandlerLifetime enforces a one-second minimum, and there is no way
        // to force a rotation without waiting for one. Worth ~1.3s for this particular invariant.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.InternalServerError);

        var services = new ServiceCollection();
        services.AddSingleton(new Marker());
        services.AddHttpClient(TestClientFactory.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddStandardResilienceHandler((o, sp) =>
            {
                _ = sp.GetRequiredService<Marker>();
                o.Retry = o.Retry with { MaxRetryAttempts = 0 };
                o.CircuitBreaker = o.CircuitBreaker with
                {
                    FailureRatioThreshold = 0.5,
                    MinimumThroughput = 2,
                    SamplingDuration = TimeSpan.FromMinutes(5),
                    BreakDuration = TimeSpan.FromMinutes(5),
                };
            })
            .SetHandlerLifetime(TimeSpan.FromSeconds(1));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Trip the breaker.
        for (var i = 0; i < 2; i++)
        {
            (await SendAsync(factory.CreateClient(TestClientFactory.ClientName))).Dispose();
        }

        await Assert.ThrowsAsync<CircuitBrokenException>(
            () => SendAsync(factory.CreateClient(TestClientFactory.ClientName)));

        var sendsBeforeRotation = stub.Sends;

        // Let the handler expire, then force a fresh chain.
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        // Still open, and still not reaching the network. Also proves the root provider the
        // configure delegate captured was not the handler's ~2-minute scope: resolving from a
        // disposed scope would throw ObjectDisposedException instead.
        await Assert.ThrowsAsync<CircuitBrokenException>(
            () => SendAsync(factory.CreateClient(TestClientFactory.ClientName)));

        Assert.Equal(sendsBeforeRotation, stub.Sends);
    }

    // ─── Keying ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_TwoCustomHandlersOnOneClient_BothApply()
    {
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
        {
            b.AddResilienceHandler("outer", pb => pb.AddTimeout(TimeSpan.FromSeconds(30)));
            b.AddResilienceHandler("inner", pb => pb.AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    ShouldHandle = HttpResiliencePredicates.IsTransient,
                }));
        });
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public void Registration_SameCustomKeyTwice_ThrowsAtRegistrationTime()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(TestClientFactory.ClientName);

        builder.AddResilienceHandler("dup", pb => pb.AddTimeout(TimeSpan.FromSeconds(1)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddResilienceHandler("dup", pb => pb.AddTimeout(TimeSpan.FromSeconds(1))));

        Assert.Contains("dup", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Registration_NullOrBlankKey_Throws(string? key)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(TestClientFactory.ClientName);

        Assert.ThrowsAny<ArgumentException>(() =>
            builder.AddResilienceHandler(key!, pb => pb.AddTimeout(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public async Task Async_CustomPipelineKey_DoesNotCollideWithAUserRegisteredPipeline()
    {
        // Ours is keyed ("api/custom", typeof(HttpResponseMessage)); a hand-registered untyped
        // pipeline under the same name is a different entry entirely.
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddResilienceHandler("custom", pb => pb.AddTimeout(TimeSpan.FromSeconds(30))),
            s => s.AddResiliencePipeline(
                $"{TestClientFactory.ClientName}/custom",
                pb => pb.AddTimeout(TimeSpan.FromSeconds(5))));
        using var scope = provider;

        using var response = await SendAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Both resolve independently.
        var registry = provider.GetRequiredService<IPipelineProvider<string>>();
        Assert.NotNull(registry.GetPipeline($"{TestClientFactory.ClientName}/custom"));
        Assert.NotNull(registry.GetPipeline<HttpResponseMessage>($"{TestClientFactory.ClientName}/custom"));
    }
}
