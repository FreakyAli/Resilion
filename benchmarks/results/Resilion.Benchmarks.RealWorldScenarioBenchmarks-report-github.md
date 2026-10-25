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
| Resilion_HttpClient_HappyPath      |        323.8 ns |         18.79 ns |         1.03 ns | 0.0696 |      - |      - |     584 B |
| Polly_HttpClient_HappyPath         |        397.2 ns |        245.68 ns |        13.47 ns |      - |      - |      - |         - |
| Resilion_DbQuery_HappyPath         |        278.4 ns |         42.13 ns |         2.31 ns | 0.0696 |      - |      - |     584 B |
| Polly_DbQuery_HappyPath            |        256.1 ns |        218.12 ns |        11.96 ns |      - |      - |      - |         - |
| Resilion_DbQuery_WithFallback      | 50,289,262.5 ns | 21,563,630.63 ns | 1,181,975.36 ns |      - |      - |      - |    2990 B |
| Resilion_Hedging_FastResponse      |     10,816.8 ns |      1,978.22 ns |       108.43 ns | 0.3510 | 0.0916 | 0.0153 |    2909 B |
| Resilion_HttpClient_Sync_HappyPath |        194.8 ns |          7.93 ns |         0.43 ns | 0.0696 |      - |      - |     584 B |
| Resilion_DbQuery_Sync_HappyPath    |        176.2 ns |         20.24 ns |         1.11 ns | 0.0696 |      - |      - |     584 B |
| Resilion_DbQuery_Sync_WithFallback | 51,463,693.0 ns | 19,411,586.55 ns | 1,064,014.56 ns |      - |      - |      - |    1867 B |
