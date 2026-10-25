using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Resilion.Extensions;
using Xunit;

namespace Resilion.Http.Tests;

/// <summary>
/// The pipeline really is a <see cref="Pipeline{TResult}"/> of
/// <see cref="HttpResponseMessage"/>, and its identity reaches telemetry.
/// </summary>
/// <remarks>
/// A typed strategy reached by an execution of a different result type now throws rather than
/// skipping itself, so a binding mistake fails loudly. These tests pin the binding anyway: a
/// skipped retry or circuit breaker is a pipeline that silently does nothing, and this is the
/// cheapest place to catch that if the guard is ever relaxed.
/// </remarks>
public class TypedStrategyBindingTests
{
    private static readonly Uri Endpoint = new("https://example.test/data");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
        => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Endpoint),
            HttpCompletionOption.ResponseHeadersRead);

    // ─── Registry keying ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_IsRegisteredAsPipelineOfHttpResponseMessage()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var (provider, _) = TestClientFactory.Build(stub, b => b.AddStandardResilienceHandler());
        using var scope = provider;

        var registry = provider.GetRequiredService<IPipelineProvider<string>>();

        Assert.NotNull(registry.GetPipeline<HttpResponseMessage>("api/standard"));

        // Typed pipelines are keyed by name AND result type, so another TResult is a miss.
        Assert.Throws<KeyNotFoundException>(() => registry.GetPipeline<string>("api/standard"));
    }

    [Fact]
    public void Pipeline_Name_IsTheClientScopedKey()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.OK);
        var (provider, _) = TestClientFactory.Build(stub, b => b.AddStandardResilienceHandler());
        using var scope = provider;

        var pipeline = provider.GetRequiredService<IPipelineProvider<string>>()
            .GetPipeline<HttpResponseMessage>("api/standard");

        Assert.Equal("api/standard", pipeline.Name);
    }

    // ─── The strategies actually bind ───────────────────────────────────────────────────────

    [Fact]
    public async Task Async_TypedRetry_IsNotSkipped_WhenExecutedThroughTheHandler()
    {
        // A result-only predicate: it can never match an exception, so the retry fires only if
        // the typed strategy genuinely ran against an HttpResponseMessage outcome.
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler("typed", pb => pb.AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    ShouldHandle = o => o.IsSuccess
                        && o.TryGetResult(out var r)
                        && r is not null
                        && r.StatusCode == HttpStatusCode.ServiceUnavailable,
                })));
        using var scope = provider;

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Sends);
    }

    [Fact]
    public async Task Async_TypedCircuitBreaker_IsNotSkipped_WhenExecutedThroughTheHandler()
    {
        var stub = StubHttpMessageHandler.WithStatuses(HttpStatusCode.ServiceUnavailable);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler("typed-cb", pb => pb.AddCircuitBreaker(
                new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    FailureRatioThreshold = 0.5,
                    MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = o => o.IsSuccess
                        && o.TryGetResult(out var r)
                        && r is not null
                        && r.StatusCode == HttpStatusCode.ServiceUnavailable,
                })));
        using var scope = provider;

        for (var i = 0; i < 2; i++)
        {
            (await SendAsync(client)).Dispose();
        }

        // Only reachable if the typed breaker recorded those result-based failures.
        await Assert.ThrowsAsync<CircuitBrokenException>(() => SendAsync(client));
    }

    // ─── Telemetry identity ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_RetryCounter_CarriesPipelineNameAndHttpMethod()
    {
        // A unique client name gives a unique pipeline.name, which is what the listener filters
        // on: MeterListener is process-global and xunit runs classes in parallel, so an
        // unfiltered listener would also count other tests' measurements.
        const string Client = "telemetry-probe-client";
        const string PipelineName = Client + "/standard";

        var observed = new List<(string? Pipeline, string? Operation)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ResilionTelemetry.MeterName
                    && instrument.Name == "resilion.retry.attempts")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? pipeline = null;
            string? operation = null;
            foreach (var tag in tags)
            {
                if (tag.Key == ResilionTelemetry.PipelineNameTag) { pipeline = tag.Value as string; }
                else if (tag.Key == ResilionTelemetry.OperationKeyTag) { operation = tag.Value as string; }
            }

            if (pipeline == PipelineName)
            {
                lock (observed) { observed.Add((pipeline, operation)); }
            }
        });
        listener.Start();

        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }),
            clientName: Client);
        using var scope = provider;

        (await SendAsync(client)).Dispose();

        List<(string? Pipeline, string? Operation)> seen;
        lock (observed) { seen = observed.ToList(); }

        Assert.NotEmpty(seen);
        Assert.All(seen, m =>
        {
            Assert.Equal(PipelineName, m.Pipeline);

            // The HTTP method, not the URI: bounded cardinality for the metric tag.
            Assert.Equal("GET", m.Operation);
        });
    }

    [Fact]
    public async Task Async_OperationKey_ReflectsTheRequestMethod()
    {
        const string Client = "telemetry-method-client";
        const string PipelineName = Client + "/standard";

        var methods = new List<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ResilionTelemetry.MeterName
                    && instrument.Name == "resilion.retry.attempts")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? pipeline = null;
            string? operation = null;
            foreach (var tag in tags)
            {
                if (tag.Key == ResilionTelemetry.PipelineNameTag) { pipeline = tag.Value as string; }
                else if (tag.Key == ResilionTelemetry.OperationKeyTag) { operation = tag.Value as string; }
            }

            if (pipeline == PipelineName)
            {
                lock (methods) { methods.Add(operation); }
            }
        });
        listener.Start();

        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(
            stub,
            b => b.AddStandardResilienceHandler(o => o.Retry = o.Retry with { Delay = RetryDelay.None }),
            clientName: Client);
        using var scope = provider;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent("body"),
        };
        (await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)).Dispose();

        List<string?> seen;
        lock (methods) { seen = methods.ToList(); }

        Assert.NotEmpty(seen);
        Assert.All(seen, m => Assert.Equal("POST", m));
    }

    // ─── The request is reachable from a strategy callback ──────────────────────────────────

    [Fact]
    public async Task Async_GetRequestMessage_ReturnsTheAttemptsRequest()
    {
        var seen = new List<Uri?>();
        var stub = StubHttpMessageHandler.WithStatuses(
            HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var (provider, client) = TestClientFactory.Build(stub, b =>
            b.AddResilienceHandler("probe", pb => pb.AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    ShouldHandle = HttpResiliencePredicates.IsTransient,
                    OnRetry = args =>
                    {
                        lock (seen) { seen.Add(args.Context.GetRequestMessage()?.RequestUri); }
                        return default;
                    },
                })));
        using var scope = provider;

        (await SendAsync(client)).Dispose();

        Assert.NotEmpty(seen);
        Assert.All(seen, uri => Assert.Equal(Endpoint, uri));
    }

    [Fact]
    public void GetRequestMessage_OutsideAnHttpPipeline_ReturnsNull()
    {
        var context = ResilienceContextPool.Shared.Rent();
        try
        {
            Assert.Null(context.GetRequestMessage());
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
