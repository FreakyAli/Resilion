```

BenchmarkDotNet v0.14.0, macOS 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD


```
| Method                             | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |-----------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| Bare_HappyPath                     |   287.4 ns |  1.37 ns |  1.28 ns |  1.00 |    0.01 | 0.1144 |      - |     960 B |        1.00 |
| Resilion_StandardHandler_HappyPath | 1,101.7 ns |  4.05 ns |  3.59 ns |  3.83 |    0.02 | 0.3128 |      - |    2624 B |        2.73 |
| Polly_StandardHandler_HappyPath    | 1,127.1 ns |  7.53 ns |  6.68 ns |  3.92 |    0.03 | 0.1450 |      - |    1224 B |        1.27 |
| Resilion_StandardHandler_OneRetry  | 1,551.5 ns |  7.11 ns |  6.31 ns |  5.40 |    0.03 | 0.3929 | 0.0019 |    3288 B |        3.42 |
| Polly_StandardHandler_OneRetry     | 1,734.8 ns | 26.01 ns | 24.33 ns |  6.04 |    0.09 | 0.1869 |      - |    1576 B |        1.64 |
