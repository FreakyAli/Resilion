```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method              | Mean     | Error    | StdDev    | Gen0   | Allocated |
|-------------------- |---------:|---------:|----------:|-------:|----------:|
| Closed_MixedTraffic | 7.398 μs | 1.034 μs | 0.0567 μs | 0.0763 |     677 B |
