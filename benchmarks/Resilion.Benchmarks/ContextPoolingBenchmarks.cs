using BenchmarkDotNet.Attributes;

namespace Resilion.Benchmarks;

/// <summary>
/// Measures the value of <see cref="ResilienceContextPool"/> — renting and returning a pooled
/// <see cref="ResilienceContext"/> vs allocating a fresh one per call. The pipeline's
/// <c>ExecuteAsync</c> overloads use the pool automatically; this isolates just that cost.
/// </summary>
[MemoryDiagnoser]
public class ContextPoolingBenchmarks
{
    /// <summary>
    /// Baseline: allocate a context directly, bypassing the pool. <see cref="ResilienceContext"/>'s
    /// constructor is internal — accessible here only via <c>InternalsVisibleTo</c> — application
    /// code should never construct one directly; this exists solely to measure what the pool saves.
    /// </summary>
    [Benchmark(Baseline = true)]
    public ResilienceContext AllocateNewContext() => new();

    [Benchmark]
    public ResilienceContext RentAndReturnFromPool()
    {
        var context = ResilienceContextPool.Shared.Rent();
        ResilienceContextPool.Shared.Return(context);
        return context;
    }
}
