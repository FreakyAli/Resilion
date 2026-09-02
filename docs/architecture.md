# Architecture

How Resilion works under the hood. Read this if you're building custom strategies, contributing to the library, or want to understand the performance characteristics.

## Core types

### Outcome\<T\>

A `readonly struct` wrapping either a successful `TResult` or a captured `Exception`. The pipeline passes outcomes as data instead of throwing and catching, avoiding stack unwinding on the hot path.

```csharp
// Success
Outcome<string>.FromResult("ok")

// Failure — exception captured, stack trace preserved
Outcome<string>.FromException(ex)

// Consumption
outcome.ThrowIfFailed();                    // rethrow with original stack trace
outcome.Match(onSuccess, onFailure);        // pattern match
outcome.TryGetResult(out var result);       // try-pattern
```

Exceptions are stored as `ExceptionDispatchInfo` so `ThrowIfFailed()` preserves the original stack trace.

### ResilienceContext

Carries execution state through the pipeline:
- `CancellationToken` — strategies may replace this (Timeout creates a linked token)
- `OperationKey` — optional name for telemetry
- `ContinueOnCapturedContext` — controls `ConfigureAwait` behavior
- `Properties` — type-safe property bag for passing data between strategies

Contexts are pooled via `ResilienceContextPool` to avoid per-call allocations. The pipeline rents one at entry and returns it at exit.

### Pipeline / Pipeline\<T\>

Two variants exist because they represent different capabilities:

- **`Pipeline`** — executes any operation. Strategies can react to exceptions only. Works with any return type because `TResult` is a method-level generic parameter.
- **`Pipeline<TResult>`** — executes operations returning `TResult`. Strategies can react to both exceptions AND result values. Required for Fallback, Hedging, and result-based predicates.

Both are sealed, immutable after construction, and thread-safe. Build once, cache, reuse.

### Strategy / Strategy\<T\>

Abstract base classes:

- **`Strategy`** — non-generic class, generic method. A single Timeout instance works with `string`, `int`, `HttpResponseMessage`, anything.
- **`Strategy<TResult>`** — generic class. A `Strategy<HttpResponseMessage>` can inspect status codes.

```csharp
// Non-generic: one instance handles any TResult
public abstract class Strategy
{
    protected internal abstract ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(...);
}

// Generic: bound to specific TResult
public abstract class Strategy<TResult>
{
    protected internal abstract ValueTask<Outcome<TResult>> ExecuteAsync(...);
}
```

## Execution model

### Pipeline chain

At `Build()` time, strategies are compiled into a linked chain of internal `PipelineComponent` objects. No allocation per call for the chain structure itself.

```text
Pipeline.ExecuteAsync(userAction, cancellationToken)
  │
  ▼
Rent ResilienceContext from pool
  │
  ▼
Wrap userAction (captures exceptions into Outcome<T>)
  │
  ▼
[Strategy1] ──next──▸ [Strategy2] ──next──▸ ... ──▸ [wrappedAction]
  │
  ▼
Unwrap Outcome<T>: success returns result, failure rethrows
  │
  ▼
Return ResilienceContext to pool
```

### Strategy ordering

First added = outermost. Canonical order:

```text
RateLimiter → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout → UserCode
```

A call flows inward through each strategy to the user delegate, then outcomes flow back outward in reverse.

### Sync vs async

Both `Execute` and `ExecuteAsync` are provided. The sync path is a true sync implementation — strategies use `Thread.Sleep` / `WaitHandle` for delays, not sync-over-async wrapping. Exception: Hedging's sync path only supports sequential mode (`HedgingDelay = Timeout.InfiniteTimeSpan`) — parallel/latency modes require concurrent execution, so `Execute()` throws `InvalidOperationException` for those rather than silently degrading to sequential.

## Performance model

See [benchmarks/results](../benchmarks/results/README.md) for measured numbers against Polly.Core.

### Happy path allocations

| Strategy | Allocations | Notes |
|----------|------------|-------|
| Retry (no retry) | 0 | Predicate check only |
| Circuit Breaker (Closed) | 0 | Volatile state read + single lock acquisition to record/read the ratio |
| Timeout (in time) | 1 CTS + 1 ITimer | CTS poolable via TryReset |
| Fallback (not triggered) | 0 | Predicate check only |
| Rate Limiter (permitted) | 1 lease | Often a struct |
| Hedging (primary wins) | 1 CTS + 1 Task | Delay timer |

### Per-call pipeline overhead

Each `StrategyComponent` in the chain creates a closure `ctx => _next.ExecuteAsync(callback, ctx)`. In a pipeline with N strategies this means N small closure allocations per call. This is inherent to the middleware pattern — documented in [future-plans.md](future-plans.md#per-call-delegate-allocation-in-pipeline-chain).

### Zero-cost telemetry

`ResilionTelemetry` creates `Meter` and `Counter<T>` instruments using BCL `System.Diagnostics.Metrics`. When no `MeterListener`, `dotnet-counters`, or OpenTelemetry SDK is attached, counter operations are no-ops at the runtime level.

## Thread safety

- **Pipeline** — immutable after construction, safe to share across threads
- **Circuit Breaker** — state machine uses a lock for transitions; failure counting delegates to the Sliding Window. Callbacks fire outside the lock.
- **Sliding Window** — a single internal lock protects bucket rotation, counter increments, and ratio computation; `RecordAndGetRatio` combines recording and reading under one acquisition for atomicity
- **ResilienceContextPool** — uses `ConcurrentBag` with approximate size cap
- **ResilienceContext** — NOT thread-safe. One context per execution, which flows through the strategy chain sequentially. Hedging creates separate contexts per attempt.
