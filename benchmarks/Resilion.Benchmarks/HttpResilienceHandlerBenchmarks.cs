using System.Net;
using System.Net.Http;
using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Resilion.RateLimiting;

namespace Resilion.Benchmarks;

/// <summary>
/// Resilion.Http's standard handler against <c>Microsoft.Extensions.Http.Resilience</c>'s, through
/// a real <see cref="IHttpClientFactory"/> graph.
/// </summary>
/// <remarks>
/// Measures handler overhead only: the primary handler answers from memory and never touches a
/// socket, so what is left is the pipeline plus the per-request bookkeeping each library adds.
/// <para>
/// Fairness notes, because the defaults differ and a silent mismatch would make this meaningless:
/// </para>
/// <list type="bullet">
/// <item>
/// Polly 8.x's standard handler enables a 1000-permit concurrency limiter by default and
/// Resilion's does not (Polly 9.x removed it too). <strong>Both arms enable one here</strong>, so
/// the comparison is five strategies against five.
/// </item>
/// <item>Retry delay is zero on both arms, so the retry benchmark measures work, not sleeping.</item>
/// <item>
/// Every benchmark disposes the response it receives, so response handling is symmetric — and
/// Resilion's handler additionally disposes the discarded ones, which is work Polly's does too.
/// </item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class HttpResilienceHandlerBenchmarks
{
    private const string Endpoint = "https://benchmark.invalid/data";

    private ServiceProvider _bareProvider = null!;
    private ServiceProvider _resilionProvider = null!;
    private ServiceProvider _pollyProvider = null!;

    private HttpClient _bare = null!;
    private HttpClient _resilion = null!;
    private HttpClient _polly = null!;

    private ServiceProvider _resilionRetryProvider = null!;
    private ServiceProvider _pollyRetryProvider = null!;
    private HttpClient _resilionRetry = null!;
    private HttpClient _pollyRetry = null!;

    /// <summary>Answers from memory, allocating one response per call in every arm alike.</summary>
    private sealed class EchoHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    /// <summary>Fails once per request, then succeeds, so exactly one retry happens.</summary>
    private sealed class FailOnceHandler : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<HttpRequestMessage, bool> _seen = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Keyed on the request instance so each benchmark iteration fails exactly once.
            // The entry is removed on the second call, otherwise the dictionary grows by one
            // request per iteration for millions of iterations — benchmark-induced Gen1/Gen2
            // pressure that lands on both arms and swamps the signal being measured.
            if (_seen.TryAdd(request, true))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            _seen.TryRemove(request, out _);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _bareProvider = BuildBare();
        _bare = _bareProvider.GetRequiredService<IHttpClientFactory>().CreateClient("bench");

        _resilionProvider = BuildResilion(retry: false);
        _resilion = _resilionProvider.GetRequiredService<IHttpClientFactory>().CreateClient("bench");

        _pollyProvider = BuildPolly(retry: false);
        _polly = _pollyProvider.GetRequiredService<IHttpClientFactory>().CreateClient("bench");

        _resilionRetryProvider = BuildResilion(retry: true);
        _resilionRetry = _resilionRetryProvider.GetRequiredService<IHttpClientFactory>().CreateClient("bench");

        _pollyRetryProvider = BuildPolly(retry: true);
        _pollyRetry = _pollyRetryProvider.GetRequiredService<IHttpClientFactory>().CreateClient("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _bareProvider.Dispose();
        _resilionProvider.Dispose();
        _pollyProvider.Dispose();
        _resilionRetryProvider.Dispose();
        _pollyRetryProvider.Dispose();
    }

    private static ServiceProvider BuildBare()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("bench")
            .ConfigurePrimaryHttpMessageHandler(() => new EchoHandler());
        return services.BuildServiceProvider();
    }

    /// <param name="retry">Selects the primary handler; the pipeline shape is identical either way.</param>
    private static ServiceProvider BuildResilion(bool retry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ConcurrencyLimiter(
            new ConcurrencyLimiterOptions { PermitLimit = 1000, QueueLimit = 0 }));

        services.AddHttpClient("bench")
            .ConfigurePrimaryHttpMessageHandler(() =>
                retry ? new FailOnceHandler() : new EchoHandler())
            .AddStandardResilienceHandler((o, sp) =>
            {
                // Matched to Polly's default, which enables one.
                o.RateLimiter = new RateLimiterStrategyOptions
                {
                    RateLimiter = sp.GetRequiredService<ConcurrencyLimiter>(),
                };
                // Always 1, never 0, on BOTH arms. Polly's options validate MaxRetryAttempts >= 1
                // and Resilion's allow 0, so disabling retry on the happy path would have given
                // Resilion a four-strategy pipeline against Polly's five — a structural advantage
                // masquerading as a faster one. Here the retry is present in both and simply does
                // not fire when the response is a 200.
                o.Retry = o.Retry with
                {
                    MaxRetryAttempts = 1,
                    Delay = RetryDelay.None,
                    UseJitter = false,
                };

                // The retry arm fails every request once, a sustained ~50% failure rate over
                // thousands of iterations, which trips both libraries' default breakers (10% over
                // 100 samples) and aborts the run. Raising the threshold to 1.0 keeps the breaker
                // in the pipeline — so it is still measured — without it ever opening.
                o.CircuitBreaker = o.CircuitBreaker with { FailureRatioThreshold = 1.0 };
            });

        return services.BuildServiceProvider();
    }

    /// <inheritdoc cref="BuildResilion(bool)"/>
    private static ServiceProvider BuildPolly(bool retry)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("bench")
            .ConfigurePrimaryHttpMessageHandler(() =>
                retry ? new FailOnceHandler() : new EchoHandler())
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 1;
                o.Retry.Delay = TimeSpan.Zero;
                o.Retry.UseJitter = false;
                o.Retry.BackoffType = Polly.DelayBackoffType.Constant;

                // Matched to the Resilion arm, and for the same reason.
                o.CircuitBreaker.FailureRatio = 1.0;
            });

        return services.BuildServiceProvider();
    }

    private static async Task<int> SendAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode;
    }

    // ── Happy path ───────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public Task<int> Bare_HappyPath() => SendAsync(_bare);

    [Benchmark]
    public Task<int> Resilion_StandardHandler_HappyPath() => SendAsync(_resilion);

    [Benchmark]
    public Task<int> Polly_StandardHandler_HappyPath() => SendAsync(_polly);

    // ── One retry ────────────────────────────────────────────────

    [Benchmark]
    public Task<int> Resilion_StandardHandler_OneRetry() => SendAsync(_resilionRetry);

    [Benchmark]
    public Task<int> Polly_StandardHandler_OneRetry() => SendAsync(_pollyRetry);
}
