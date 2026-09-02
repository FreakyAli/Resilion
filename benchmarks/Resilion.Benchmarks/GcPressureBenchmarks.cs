using BenchmarkDotNet.Attributes;
using Polly;

namespace Resilion.Benchmarks;

/// <summary>
/// Runs a large batch of executions in a single invocation to surface GC pressure (Gen0/Gen1
/// collections reported by <see cref="MemoryDiagnoserAttribute"/>) under sustained load, rather
/// than just the per-call allocation size the other benchmark classes measure.
/// </summary>
[MemoryDiagnoser]
public class GcPressureBenchmarks
{
    private const int Iterations = 100_000;

    private Resilion.Pipeline _resilionRetry = null!;
    private ResiliencePipeline _pollyRetry = null!;

    [GlobalSetup]
    public void Setup()
    {
        _resilionRetry = Resilion.Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
        }));

        _pollyRetry = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
            })
            .Build();
    }

    [Benchmark(Baseline = true)]
    public async Task Resilion_100k_HappyPathExecutions()
    {
        for (var i = 0; i < Iterations; i++)
        {
            await _resilionRetry.ExecuteAsync(
                static (state, ct) => new ValueTask<string>("ok"),
                (object?)null);
        }
    }

    [Benchmark]
    public async Task Polly_100k_HappyPathExecutions()
    {
        for (var i = 0; i < Iterations; i++)
        {
            await _pollyRetry.ExecuteAsync(
                static (state, ct) => new ValueTask<string>("ok"),
                (object?)null);
        }
    }
}
