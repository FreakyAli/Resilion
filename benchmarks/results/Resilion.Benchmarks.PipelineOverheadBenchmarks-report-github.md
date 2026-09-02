```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                       | Mean        | Error      | StdDev    | Ratio     | RatioSD  | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |------------:|-----------:|----------:|----------:|---------:|-------:|----------:|------------:|
| DirectCall                   |   0.0770 ns |  0.7340 ns | 0.0402 ns |      1.26 |     0.93 |      - |         - |          NA |
| Resilion_Empty               |  69.4926 ns |  6.6621 ns | 0.3652 ns |  1,137.25 |   609.03 | 0.0114 |      96 B |          NA |
| Polly_Empty                  |  58.5517 ns |  3.0302 ns | 0.1661 ns |    958.20 |   513.13 |      - |         - |          NA |
| Resilion_Retry_HappyPath     | 113.8271 ns |  7.8695 ns | 0.4314 ns |  1,862.78 |   997.55 | 0.0229 |     192 B |          NA |
| Polly_Retry_HappyPath        | 165.4505 ns | 18.3020 ns | 1.0032 ns |  2,707.60 | 1,450.02 |      - |         - |          NA |
| Resilion_Composite_HappyPath | 392.8716 ns | 43.2298 ns | 2.3696 ns |  6,429.34 | 3,443.15 | 0.1163 |     976 B |          NA |
| Polly_Composite_HappyPath    | 733.9476 ns | 20.2942 ns | 1.1124 ns | 12,011.05 | 6,432.00 |      - |         - |          NA |
| Resilion_Retry_Sync          |  61.1025 ns | 31.5703 ns | 1.7305 ns |    999.94 |   536.18 | 0.0229 |     192 B |          NA |
