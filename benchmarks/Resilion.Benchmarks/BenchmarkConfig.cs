using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Resilion.Benchmarks;

/// <summary>
/// Shared configuration for all Resilion benchmarks.
/// Ensures consistent memory diagnostics across runs.
/// When built as multi-target (net8.0;net10.0), BenchmarkDotNet automatically
/// runs against each runtime.
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Memory diagnostics to catch allocation regressions
        AddDiagnoser(MemoryDiagnoser.Default);

        // Baseline for visual comparison
        WithOption(ConfigOptions.DisableOptimizationsValidator, true);
    }
}
