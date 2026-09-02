using BenchmarkDotNet.Attributes;
using Polly;

namespace Resilion.Benchmarks;

/// <summary>
/// Measures the overhead of executing through a pipeline vs a direct call.
/// Answers: "What tax does Resilion add on the happy path?"
/// </summary>
[MemoryDiagnoser]
public class PipelineOverheadBenchmarks
{
    private Resilion.Pipeline _resilionEmpty = null!;
    private Resilion.Pipeline _resilionRetry = null!;
    private Resilion.Pipeline _resilionComposite = null!;
    private ResiliencePipeline _pollyEmpty = null!;
    private ResiliencePipeline _pollyRetry = null!;
    private ResiliencePipeline _pollyComposite = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Resilion pipelines
        _resilionEmpty = Resilion.Pipeline.Empty;

        _resilionRetry = Resilion.Pipeline.Create(b => b
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
            }));

        _resilionComposite = Resilion.Pipeline.Create(b => b
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 100,
            })
            .AddTimeout(TimeSpan.FromSeconds(5)));

        // Polly pipelines
        _pollyEmpty = ResiliencePipeline.Empty;

        _pollyRetry = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
            })
            .Build();

        _pollyComposite = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 100,
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }

    // ── Direct call (baseline) ───────────────────────────────────

    [Benchmark(Baseline = true)]
    public ValueTask<string> DirectCall()
    {
        return new ValueTask<string>("ok");
    }

    // ── Empty pipeline (no strategies) ───────────────────────────

    [Benchmark]
    public ValueTask<string> Resilion_Empty()
    {
        return _resilionEmpty.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    [Benchmark]
    public ValueTask<string> Polly_Empty()
    {
        return _pollyEmpty.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    // ── Single retry strategy (happy path, no failures) ──────────

    [Benchmark]
    public ValueTask<string> Resilion_Retry_HappyPath()
    {
        return _resilionRetry.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    [Benchmark]
    public ValueTask<string> Polly_Retry_HappyPath()
    {
        return _pollyRetry.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    // ── Composite pipeline (happy path) ──────────────────────────

    [Benchmark]
    public ValueTask<string> Resilion_Composite_HappyPath()
    {
        return _resilionComposite.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    [Benchmark]
    public ValueTask<string> Polly_Composite_HappyPath()
    {
        return _pollyComposite.ExecuteAsync(
            static (state, ct) => new ValueTask<string>("ok"),
            (object?)null);
    }

    // ── Sync execution ───────────────────────────────────────────

    [Benchmark]
    public string Resilion_Retry_Sync()
    {
        return _resilionRetry.Execute(
            static (state, ct) => "ok",
            (object?)null);
    }
}
