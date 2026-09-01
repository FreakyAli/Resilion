using BenchmarkDotNet.Running;
using Resilion.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PipelineOverheadBenchmarks).Assembly).Run(args);
