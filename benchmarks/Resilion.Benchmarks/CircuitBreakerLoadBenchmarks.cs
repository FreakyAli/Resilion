using BenchmarkDotNet.Attributes;

namespace Resilion.Benchmarks;

/// <summary>
/// Circuit breaker in the Closed state handling mixed success/failure traffic. Every call
/// exercises <c>SlidingWindow.RecordAndGetRatio</c> under its single lock — the strategy's
/// hot-path contention point.
/// </summary>
[MemoryDiagnoser]
public class CircuitBreakerLoadBenchmarks
{
    private Resilion.Pipeline _pipeline = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        // High threshold and throughput floor — this traffic mix never trips the circuit,
        // so every call takes the Closed-state recording path being measured.
        _pipeline = Resilion.Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.8,
            MinimumThroughput = 1000,
            SamplingDuration = TimeSpan.FromSeconds(60),
        }));
    }

    /// <summary>~20% of calls fail — comfortably under the 80% threshold.</summary>
    [Benchmark]
    public async ValueTask<string> Closed_MixedTraffic()
    {
        var i = Interlocked.Increment(ref _counter);
        try
        {
            return await _pipeline.ExecuteAsync<string>(ct =>
                i % 5 == 0
                    ? throw new InvalidOperationException("simulated failure")
                    : new ValueTask<string>("ok"));
        }
        catch (InvalidOperationException)
        {
            return "handled";
        }
    }
}
