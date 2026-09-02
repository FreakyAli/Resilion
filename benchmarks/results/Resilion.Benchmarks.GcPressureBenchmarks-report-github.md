```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Allocated  | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|-----------:|------------:|
| Resilion_100k_HappyPathExecutions | 11.44 ms | 1.862 ms | 0.102 ms |  1.00 | 2281.2500 | 19200012 B |       1.000 |
| Polly_100k_HappyPathExecutions    | 16.47 ms | 0.451 ms | 0.025 ms |  1.44 |         - |       23 B |       0.000 |
