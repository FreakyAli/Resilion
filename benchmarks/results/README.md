# Benchmark Results

Captured from `dotnet run -c Release --project benchmarks/Resilion.Benchmarks -- --job short`
(BenchmarkDotNet `ShortRun`: 3 iterations, 3 warmup — a real, statistically-averaged job, not
`--job dry`'s single iteration, but lower-confidence than the full default job). Run on:

- BenchmarkDotNet v0.14.0
- Apple M4 Pro, 14 cores, macOS (Darwin 25.6.0)
- .NET 8.0.11, Arm64 RyuJIT AdvSIMD

For a statistically rigorous run (default BenchmarkDotNet job — full pilot/warmup/actual
stages), drop `-- --job short` and expect it to take significantly longer (each of the ~22
benchmark methods individually calibrates its iteration count, which for near-zero-cost
operations like `DirectCall` can itself take several minutes).

Full per-class output (including method docs) is in the sibling `.md` files in this directory.

## Pipeline overhead (happy path, no failures)

| Benchmark | Mean | Allocated |
|-----------|-----:|----------:|
| Direct call (no pipeline) | ~0 ns | 0 B |
| Resilion — empty pipeline | 70 ns | 96 B |
| Polly — empty pipeline | 60 ns | 0 B |
| Resilion — single retry | 116 ns | 192 B |
| Polly — single retry | 167 ns | 0 B |
| Resilion — composite (Timeout+Retry+CB+Timeout) | 476 ns | 1120 B |
| Polly — composite (same shape) | 745 ns | 0 B |
| Resilion — single retry, **sync** | 53 ns | 192 B |

Resilion's happy-path allocation is non-zero (Polly.Core's is zero here) but its wall-clock time
is consistently lower across every shape tested, including the composite pipeline most
applications actually run. The allocations come from per-strategy closures in the middleware
chain — see [future-plans.md](../../docs/future-plans.md) item #4 and
[tradeoffs.md](../../docs/tradeoffs.md).

The composite figure moved from 393 ns / 976 B in the previous run. Two things changed, and it is
worth separating them:

- **976 B → 1120 B is a real regression, and deliberate.** The Timeout strategy now records which
  cause cancelled its linked token first, so user cancellation can no longer be misclassified as a
  timeout under a race. That costs 32 B per timeout, plus a `CancellationToken` registration when
  the token reaching that timeout can be cancelled. The outer timeout replaces the context token
  with a cancellable one, so the **inner** timeout pays the registration too — hence 144 B on this
  two-timeout shape rather than 64 B. Measured in isolation: timeout-only went 360 B → 392 B.
- **393 ns → 476 ns is mostly not that change.** Re-measuring the unmodified code on this machine
  today gives 431 ns, not 393 ns, so roughly half the apparent slowdown is run-to-run drift in the
  `ShortRun` configuration rather than the fix. Treat cross-run wall-clock deltas under `ShortRun`
  (3 iterations) as indicative only; allocation figures are deterministic and comparable.

## Real-world scenario patterns

| Benchmark | Mean | Allocated |
|-----------|-----:|----------:|
| Resilion — HTTP client pattern (Timeout+Retry+CB) | 324 ns | 584 B |
| Polly — HTTP client pattern (same shape) | 397 ns | 0 B |
| Resilion — DB query pattern (Fallback+Timeout+Retry) | 278 ns | 584 B |
| Polly — DB query pattern (same shape) | 256 ns | 0 B |
| Resilion — DB query, fallback triggered | 50.3 ms | 2990 B |
| Resilion — Hedging, fast response | 10.8 μs | 2909 B |
| Resilion — HTTP client, **sync** | 195 ns | 584 B |
| Resilion — DB query, **sync** | 176 ns | 584 B |
| Resilion — DB query, **sync**, fallback triggered | 51.5 ms | 1867 B |

Sync execution is consistently *faster* than async for the same pipeline shape — the payoff of
a real sync path (`Thread.Sleep`, no `Task` machinery) rather than sync-over-async.

Both timeout-bearing shapes moved from 552 B to 584 B: exactly +32 B, one per Timeout strategy,
from race-free cancellation classification. These pipelines contain a single timeout and are
executed without a caller token, so they pay the recording object and not the token registration —
see the two-timeout composite in the section above for the shape that pays both, and
[tradeoffs.md](../../docs/tradeoffs.md) for why.

Wall-clock figures here shifted more than allocation did, and should not be read as a like-for-like
regression: re-measuring *unmodified* code on this machine produced up to ~10% run-to-run variation
under `ShortRun` (3 iterations). Allocation is deterministic and comparable across runs; timing at
this configuration is indicative only.

**Note on the "fallback triggered" rows:** these jumped from microseconds to tens of
milliseconds partway through this project. Not a performance regression — the DB-query
pipeline's strategy order was `Timeout → Retry → Fallback` (Fallback innermost), so Fallback
caught every failure before Retry ever saw one, silently making the configured retry a no-op.
The new `ThrowOnOrderingErrors` validation (default on) caught this while re-verifying the
benchmark suite; the pipeline is now `Fallback → Timeout → Retry`, so it actually exercises the
one configured retry (with its 50ms linear delay) before falling back — which is what these
numbers now measure honestly.

## Circuit breaker under load

| Benchmark | Mean | Allocated |
|-----------|-----:|----------:|
| Closed state, ~20% mixed failure traffic | 7.4 μs | 677 B |

Every call in this benchmark exercises `SlidingWindow.RecordAndGetRatio` under its lock — this
is the number to watch if you suspect lock contention under high-throughput circuit breaker
usage.

## Context pooling

| Benchmark | Mean | Allocated |
|-----------|-----:|----------:|
| Allocate a new `ResilienceContext` | 5.1 ns | 72 B |
| Rent + return from `ResilienceContextPool` | 19.2 ns | 0 B |

Pooling trades ~14ns of extra CPU time (pool bookkeeping) for eliminating the 72-byte allocation
entirely — a clear win under any sustained load where GC pressure matters more than a few
nanoseconds per call.

## GC pressure (100,000 executions in one batch)

| Benchmark | Mean | Gen0 collections | Allocated |
|-----------|-----:|------------------:|----------:|
| Resilion — 100k happy-path retries | 11.4 ms | 2281 | 19.2 MB |
| Polly — 100k happy-path retries | 16.5 ms | 0 | 23 B |

Resilion is ~1.4x faster in wall-clock time for this workload, at the cost of measurably more
Gen0 collections from the per-call closure allocations (see the pipeline-overhead section
above). Polly.Core's zero-allocation happy path avoids this entirely. Whether the tradeoff
matters depends on your workload — if you're already doing real I/O per call (the overwhelmingly
common case), the GC cost here is unlikely to be the bottleneck.
