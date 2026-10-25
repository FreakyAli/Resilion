```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                       | Mean        | Error       | StdDev     | Median      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |------------:|------------:|-----------:|------------:|------:|--------:|-------:|----------:|------------:|
| DirectCall                   |   0.0064 ns |   0.2014 ns |  0.0110 ns |   0.0000 ns |     ? |       ? |      - |         - |           ? |
| Resilion_Empty               |  69.7421 ns |   3.3161 ns |  0.1818 ns |  69.6444 ns |     ? |       ? | 0.0114 |      96 B |           ? |
| Polly_Empty                  |  59.9433 ns |   0.3970 ns |  0.0218 ns |  59.9489 ns |     ? |       ? |      - |         - |           ? |
| Resilion_Retry_HappyPath     | 115.7802 ns |   4.0554 ns |  0.2223 ns | 115.8675 ns |     ? |       ? | 0.0229 |     192 B |           ? |
| Polly_Retry_HappyPath        | 166.6076 ns |   5.7315 ns |  0.3142 ns | 166.4953 ns |     ? |       ? |      - |         - |           ? |
| Resilion_Composite_HappyPath | 476.2153 ns | 292.6871 ns | 16.0432 ns | 485.1351 ns |     ? |       ? | 0.1335 |    1120 B |           ? |
| Polly_Composite_HappyPath    | 744.6206 ns | 270.1220 ns | 14.8063 ns | 737.3242 ns |     ? |       ? |      - |         - |           ? |
| Resilion_Retry_Sync          |  53.1058 ns |   7.1679 ns |  0.3929 ns |  52.9346 ns |     ? |       ? | 0.0229 |     192 B |           ? |
