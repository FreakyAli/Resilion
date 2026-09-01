using BenchmarkDotNet.Attributes;
using Polly;

namespace Resilion.Benchmarks;

/// <summary>
/// Real-world scenario benchmarks comparing Resilion with Polly.Core.
/// Measures performance in realistic patterns: HTTP clients, database queries, hedging.
/// </summary>
[MemoryDiagnoser]
public class RealWorldScenarioBenchmarks
{
    private Resilion.Pipeline<string> _resilionHttpClient = null!;
    private Resilion.Pipeline<string> _resilionDbQuery = null!;
    private Resilion.Pipeline<string> _resilionHedging = null!;
    private ResiliencePipeline<string> _pollyHttpClient = null!;
    private ResiliencePipeline<string> _pollyDbQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ────── Resilion: HTTP Client Pattern (Timeout + Retry + Circuit Breaker) ──────
        // Typical for: API calls, microservice communication
        _resilionHttpClient = Resilion.Pipeline.Create<string>(b => b
            .AddTimeout(TimeSpan.FromSeconds(5))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100)),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = 0.25,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(5),
            }));

        // ────── Resilion: Database Query Pattern (Timeout + Retry + Fallback) ──────
        // Typical for: Database reads with cache fallback
        _resilionDbQuery = Resilion.Pipeline.Create<string>(b => b
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.Linear(TimeSpan.FromMilliseconds(50)),
            })
            .AddFallback(new FallbackStrategyOptions<string>
            {
                FallbackAction = "CACHED_DEFAULT"
            }));

        // ────── Resilion: Hedging Pattern (Parallel attempts, fail fast) ──────
        // Typical for: Latency-sensitive operations, request fan-out
        _resilionHedging = Resilion.Pipeline.Create<string>(b => b
            .AddTimeout(TimeSpan.FromSeconds(5))
            .AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.FromMilliseconds(250),
            }));

        // ────── Polly: HTTP Client Pattern ──────
        _pollyHttpClient = new ResiliencePipelineBuilder<string>()
            .AddTimeout(TimeSpan.FromSeconds(5))
            .AddRetry(new Polly.Retry.RetryStrategyOptions<string>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential,
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions<string>
            {
                FailureRatio = 0.25,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(5),
            })
            .Build();

        // ────── Polly: Database Query Pattern ──────
        _pollyDbQuery = new ResiliencePipelineBuilder<string>()
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new Polly.Retry.RetryStrategyOptions<string>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Linear,
            })
            .Build();
    }

    // ────────────────────────────────────────────────────────────
    // HTTP Client Pattern (API calls, microservice communication)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resilion: HTTP request happy path through timeout + retry + circuit breaker.
    /// </summary>
    [Benchmark]
    public ValueTask<string> Resilion_HttpClient_HappyPath()
    {
        return _resilionHttpClient.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("OK"),
            (object?)null);
    }

    /// <summary>
    /// Polly: HTTP request happy path through same pipeline.
    /// </summary>
    [Benchmark]
    public ValueTask<string> Polly_HttpClient_HappyPath()
    {
        return _pollyHttpClient.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("OK"),
            (object?)null);
    }

    // ────────────────────────────────────────────────────────────
    // Database Query Pattern (read with fallback to cache)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resilion: Database query happy path through timeout + retry + fallback.
    /// </summary>
    [Benchmark]
    public ValueTask<string> Resilion_DbQuery_HappyPath()
    {
        return _resilionDbQuery.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("user@example.com"),
            (object?)null);
    }

    /// <summary>
    /// Polly: Database query happy path through same pipeline.
    /// </summary>
    [Benchmark]
    public ValueTask<string> Polly_DbQuery_HappyPath()
    {
        return _pollyDbQuery.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("user@example.com"),
            (object?)null);
    }

    /// <summary>
    /// Resilion: Database query triggers fallback (simulates failure).
    /// </summary>
    [Benchmark]
    public ValueTask<string> Resilion_DbQuery_WithFallback()
    {
        return _resilionDbQuery.ExecuteAsync(
            static (state, ct) => throw new TimeoutException("DB timeout"),
            (object?)null);
    }


    // ────────────────────────────────────────────────────────────
    // Hedging Pattern (parallel attempts for latency reduction)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resilion: Hedging fast path (first attempt succeeds quickly).
    /// No benefit from hedging here, just overhead.
    /// </summary>
    [Benchmark]
    public ValueTask<string> Resilion_Hedging_FastResponse()
    {
        return _resilionHedging.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("OK"),
            (object?)null);
    }

    // ────────────────────────────────────────────────────────────
    // Synchronous Execution (true sync, not sync-over-async)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resilion: Synchronous HTTP request (true blocking implementation).
    /// Important for ASP.NET Framework, sync middleware, and backward compatibility.
    /// </summary>
    [Benchmark]
    public string Resilion_HttpClient_Sync_HappyPath()
    {
        return _resilionHttpClient.Execute(
            static (state, ct) => "OK",
            (object?)null);
    }

    /// <summary>
    /// Resilion: Synchronous database query happy path.
    /// </summary>
    [Benchmark]
    public string Resilion_DbQuery_Sync_HappyPath()
    {
        return _resilionDbQuery.Execute(
            static (state, ct) => "user@example.com",
            (object?)null);
    }

    /// <summary>
    /// Resilion: Synchronous database query with fallback triggered.
    /// </summary>
    [Benchmark]
    public string Resilion_DbQuery_Sync_WithFallback()
    {
        return _resilionDbQuery.Execute(
            static (state, ct) => throw new TimeoutException("DB timeout"),
            (object?)null);
    }
}
