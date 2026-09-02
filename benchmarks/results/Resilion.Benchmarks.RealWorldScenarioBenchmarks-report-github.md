```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                             | Mean            | Error            | StdDev          | Gen0   | Gen1   | Gen2   | Allocated |
|----------------------------------- |----------------:|-----------------:|----------------:|-------:|-------:|-------:|----------:|
| Resilion_HttpClient_HappyPath      |        266.6 ns |         24.49 ns |         1.34 ns | 0.0658 |      - |      - |     552 B |
| Polly_HttpClient_HappyPath         |        390.5 ns |         20.09 ns |         1.10 ns |      - |      - |      - |         - |
| Resilion_DbQuery_HappyPath         |        277.2 ns |         50.27 ns |         2.76 ns | 0.0658 |      - |      - |     552 B |
| Polly_DbQuery_HappyPath            |        253.9 ns |         28.25 ns |         1.55 ns |      - |      - |      - |         - |
| Resilion_DbQuery_WithFallback      | 48,003,929.3 ns | 22,548,513.09 ns | 1,235,960.09 ns |      - |      - |      - |    2988 B |
| Resilion_Hedging_FastResponse      |     10,672.8 ns |     22,537.33 ns |     1,235.35 ns | 0.3662 | 0.1221 | 0.0458 |    2838 B |
| Resilion_HttpClient_Sync_HappyPath |        146.0 ns |          1.23 ns |         0.07 ns | 0.0658 |      - |      - |     552 B |
| Resilion_DbQuery_Sync_HappyPath    |        145.8 ns |         77.91 ns |         4.27 ns | 0.0658 |      - |      - |     552 B |
| Resilion_DbQuery_Sync_WithFallback | 54,443,839.5 ns | 54,422,093.11 ns | 2,983,058.57 ns |      - |      - |      - |    1849 B |
