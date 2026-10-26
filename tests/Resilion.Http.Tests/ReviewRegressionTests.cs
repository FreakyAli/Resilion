using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// Regressions found in review of the 1.0 branch.
/// </summary>
public class ReviewRegressionTests
{
    [Fact]
    public async Task CloneRequest_OnRetryCallback_SeesALiveRequestNotTheDisposedClone()
    {
        // The per-attempt clone is disposed in SendAttemptAsync's finally, but callbacks run
        // after that returns. Leaving the clone in context.Properties handed OnRetry a disposed
        // message, so reading its content threw ObjectDisposedException.
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        // Declared before the lambda so the closure can capture it; assigned before SendAsync.
        HttpRequestMessage? originalMessage = null;
        HttpRequestMessage? observedRequest = null;

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddStandardResilienceHandler(o =>
            {
                o.RequestReplay = HttpRequestReplay.CloneRequest;
                o.Retry = o.Retry with
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    OnRetry = args =>
                    {
                        // Capture for identity check after the call completes.
                        observedRequest = args.Context.GetRequestMessage();
                        return default;
                    },
                };
            }));

        using var scope = provider;

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x")
        {
            Content = JsonContent.Create(new { value = 1 }),
        };
        originalMessage = message;

        (await client.SendAsync(message)).Dispose();

        // The context must expose the caller's live request, not the disposed per-attempt clone.
        Assert.True(ReferenceEquals(observedRequest, originalMessage),
            "OnRetry received a different HttpRequestMessage instance than the caller's original.");
    }

    [Fact]
    public async Task ConfigureDelegate_IsNotReRunOnHandlerRotation()
    {
        // Resolving the replay mode re-runs the caller's configure delegate, and the handler
        // factory re-runs on every IHttpClientFactory rotation — so without caching it ran for
        // the life of the app, taking any side effect in the delegate with it.
        //
        // Deliberately slow, for the same reason as Async_CircuitBreakerState_SurvivesHandlerRotation:
        // SetHandlerLifetime enforces a one-second minimum and rotation cannot be forced.
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK);

        var configureCalls = 0;

        var services = new ServiceCollection();
        services.AddHttpClient(TestClientFactory.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddStandardResilienceHandler(_ => Interlocked.Increment(ref configureCalls))
            .SetHandlerLifetime(TimeSpan.FromSeconds(1));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        (await factory.CreateClient(TestClientFactory.ClientName)
            .GetAsync("https://example.test/x")).Dispose();

        // The exact baseline is an implementation detail — the delegate runs while the pipeline
        // is built and again while the replay mode is probed, and the factory may construct the
        // chain more than once on first use. What matters is that it stops there.
        var afterFirst = Volatile.Read(ref configureCalls);
        Assert.True(afterFirst > 0, "configure should have run at least once");

        // Let the handler chain expire so the next client builds a fresh one.
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        (await factory.CreateClient(TestClientFactory.ClientName)
            .GetAsync("https://example.test/y")).Dispose();

        // Unchanged: the rotation must not re-run the delegate.
        Assert.Equal(afterFirst, Volatile.Read(ref configureCalls));
    }


}
