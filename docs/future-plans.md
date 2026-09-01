# Future Plans

Features, improvements, and known tradeoffs under consideration for future versions of Resilion. Each section includes enough detail to serve as a starting point for implementation.

## Type Classification

| Type | Meaning | Impact | Examples |
|------|---------|--------|----------|
| **Feature** | New capability that extends Resilion's functionality beyond current scope | Additive — users gain new resilience options or integrations | Rate Limiter, Hedging, DI integration, Telemetry |
| **Fix** | Correctness issue, performance bug, or missing validation in existing features | Blocking/Correctness — existing code may behave incorrectly or sub-optimally | Per-call closure allocation, WaitHandle allocation |
| **Documentation** | Gaps in docs, missing behavioral explanations, or unclear semantics | Clarity — improves developer experience and correctness of usage | Strategy ordering guide, cancellation semantics |
| **Improvement** | Enhancement to an existing feature that doesn't change its API contract | Quality — existing code works but could work better | CTS pooling, delegate caching |
| **Infrastructure** | CI/CD, packaging, tooling, or repository quality improvements | Adoption — removes friction for users and contributors | Benchmarks, icon, README |

**How to use this guide:**
- **Fixes** should be prioritized over Features if they affect correctness
- **Documentation** should accompany every Feature (document what it does and why)
- New contributors reading this can quickly understand priority and scope

## Priority Matrix

| # | Item | Priority | Type | Impact | Effort | Notes |
|---|------|----------|------|--------|--------|-------|
| 3 | Resilion.Extensions (DI + telemetry) | P1 | Feature | High | Medium | Microsoft.Extensions integration |
| 4 | Per-call delegate allocation in pipeline chain | P2 | Fix | Medium | High | Middleware pattern inherent cost |
| 5 | CancellationTokenSource pooling for Timeout/Hedging | P2 | Improvement | Medium | Medium | Reduce alloc on hot path |
| 6 | Pipeline ordering validation | P2 | Feature | Medium | Medium | Warn on common misorderings |
| 7 | Benchmarks vs Polly | P2 | Infrastructure | High | Medium | Prove performance claims |
| 8 | README and docs | P2 | Documentation | High | Medium | User-facing documentation |
| 9 | WaitHandle allocation in sync retry | P3 | Fix | Low | Low | CancellationToken.WaitHandle lazy alloc |
| 10 | Resilion.Testing package | P3 | Feature | Medium | Low | Test doubles, assertion helpers |
| 11 | Hedging: Task.WhenAny memory leak | P2 | Fix | Medium | Medium | Continuations not deregistered on losing tasks |
| 12 | Hedging: HedgingDelayGenerator | P3 | Feature | Low | Low | Dynamic delay per attempt |
| 13 | Hedging: Properties propagation to attempt contexts | P2 | Fix | Medium | Low | User properties not copied to hedged attempts |
| 14 | Hedging: HedgingRejectedException with aggregated errors | P3 | Improvement | Low | Low | Currently dead code — never thrown |

> **Completed (removed from backlog):** Solution structure, .editorconfig, global.json, CI workflows, Outcome\<T\>, ResilienceContext pooling, Strategy base types, Pipeline/PipelineBuilder, ResilienceEventHandler union struct, Timeout strategy, Retry strategy (with RetryDelay discriminated union), Circuit Breaker (sliding window, state machine, thread safety), Fallback (with FallbackAction implicit conversions), code review and fixes for 13 issues, Rate Limiter strategy (wrapping System.Threading.RateLimiting), Hedging strategy (parallel/latency/sequential modes, per-attempt CTS, cleanup-on-cancel).

---

## P1 — Do First

### 3. Resilion.Extensions (DI + Telemetry)

**Type:** Feature — Integration Package

**Why**

Production services use `Microsoft.Extensions.DependencyInjection` and OpenTelemetry. Resilion needs first-class integration without forcing these dependencies on the core package.

**Design**

The `Resilion.Extensions` project (already exists) will provide:
- `IServiceCollection.AddResilion()` and `AddResiliencePipeline(name, configure)` extensions
- `ResiliencePipelineProvider<TKey>` for resolving named/keyed pipelines
- `Meter("Resilion")` and `ActivitySource("Resilion")` for OpenTelemetry-compatible metrics and tracing
- `ILogger` integration for strategy events

**Files to Modify/Create:**
- `src/Resilion.Extensions/ResilionServiceCollectionExtensions.cs`
- `src/Resilion.Extensions/PipelineRegistry.cs`
- `src/Resilion.Extensions/Telemetry/ResilionMeter.cs`
- `tests/Resilion.Extensions.Tests/` — DI registration, telemetry emission tests

---

## P2 — Do Next

### 4. Per-Call Delegate Allocation in Pipeline Chain

**Type:** Fix — Performance

**Why**

In `StrategyComponent.ExecuteAsync`, the lambda `ctx => _next.ExecuteAsync(callback, ctx)` creates a closure capturing `_next` and `callback` on every call. In a pipeline with N strategies, this means N small closure allocations per execution.

**Current State**

This is inherent to the middleware/chain-of-responsibility pattern. Polly v8 has the same cost. The allocations are small (one closure object + one delegate per strategy per call) and negligible compared to real I/O costs.

**Potential Fix**

Pre-compose the delegate chain at build time into a single `Func<ResilienceContext, Func<...>, ValueTask<Outcome<T>>>` that doesn't allocate per call. This requires complex generic type threading and may not be feasible without sacrificing API simplicity.

**Decision:** Accept for now. Benchmark to confirm the cost is negligible relative to real workloads. Revisit if benchmarks show it matters.

---

### 5. CancellationTokenSource Pooling for Timeout

**Type:** Improvement — Performance

**Why**

The Timeout strategy allocates one `CancellationTokenSource` per execution. `CancellationTokenSource.TryReset()` (available since .NET 6) enables pooling — reset and reuse instead of allocating.

**Design**

Use `ObjectPool<CancellationTokenSource>` with a custom policy that calls `TryReset()` on return. If `TryReset()` returns false (the CTS was cancelled), discard instead of pooling.

**Complexity:** Medium. `TryReset()` has edge cases around timer state and concurrent cancellation.

**Decision:** Implement after benchmarks confirm the CTS allocation is a meaningful cost.

---

### 6. Pipeline Ordering Validation

**Type:** Feature — Developer Experience

**Why**

Strategy ordering dramatically affects behavior. Common misorderings (Retry outside CircuitBreaker, missing per-attempt timeout) are a real source of bugs. Polly v8 has no validation.

**Design**

`Build()` performs heuristic validation and emits diagnostic warnings for patterns that are usually wrong:
- Retry outside CircuitBreaker (retries bypass the breaker)
- Fallback not outermost (unusual)
- Multiple Timeout strategies without one being outermost
- Hedging and Retry both present (may cause excessive load)

Warnings, not errors. Suppressible via `builder.SuppressOrderingWarnings = true`.

---

### 7. Benchmarks vs Polly

**Type:** Infrastructure — Performance Validation

**Why**

Resilion claims to be high-performance. Without benchmarks, that's marketing.

**Design**

The benchmark project (`benchmarks/Resilion.Benchmarks/`) already exists. Add:
- Pipeline overhead (no-op vs direct call)
- Per-strategy happy path (no trigger)
- Allocation profile (`[MemoryDiagnoser]`)
- Polly.Core head-to-head comparison
- Composition depth scaling (1, 2, 3, 5 strategies)

---

### 11. Hedging: Task.WhenAny Memory Leak

**Type:** Fix — Performance/Correctness

**Why**

`WaitForBestOutcome` uses `Task.WhenAny(remaining)` in a loop, removing completed tasks from the list. The standard `Task.WhenAny` implementation registers a continuation on every task in the list. When a task completes, the continuations on the *other* tasks are not deregistered — they hold references to the `Task.WhenAny` result task and its captured state, preventing garbage collection until all tasks in the list complete.

In a pipeline with 3+ hedged attempts where the first wins quickly, the losing tasks' continuations hold memory until they eventually complete or are cancelled.

**Potential Fix**

Implement a custom `FirstCompletedAsync` that uses `TaskCompletionSource` + per-task continuations that deregister themselves on completion. Or use `WaitAsync` chaining.

**Impact:** Medium. Only affects memory under hedging with many concurrent attempts. The cleanup in `finally` (awaiting all tasks) limits the leak duration.

---

### 13. Hedging: Properties Propagation to Attempt Contexts

**Type:** Fix — Correctness

**Why**

In `HedgingStrategy.ResolveAsyncAction`, when re-executing the original callback for hedged attempts, the code creates a new `ResilienceContext` and copies `OperationKey` and `ContinueOnCapturedContext` but does NOT copy `Properties`. If the user stored custom data in `context.Properties` before execution (or an outer strategy set properties), hedged attempts won't see it.

**Fix:** Copy all properties from the original context to the attempt context. Either iterate and copy, or add a `CopyFrom` method on `ResilienceProperties`.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `ResolveAsyncAction`, line ~210

---

## P3 — Backlog

### 9. WaitHandle Allocation in Sync Retry

**Type:** Fix — Performance (minor)

**Why**

The sync retry path uses `context.CancellationToken.WaitHandle.WaitOne(delay)` for cancellation-aware sleep. Accessing `CancellationToken.WaitHandle` lazily allocates a `ManualResetEvent` that is never explicitly disposed. This happens on every retry delay in the sync path.

**Potential Fix**

Use `SpinWait` with periodic `IsCancellationRequested` checks for short delays, and `ManualResetEventSlim` with explicit disposal for longer delays. Or use `TimeProvider.CreateTimer` with a `ManualResetEventSlim` signal.

**Decision:** Low priority. The sync retry path is not the hot path in most applications. Revisit if sync usage proves significant.

---

### 10. Resilion.Testing Package

**Type:** Feature — Test Infrastructure

**Why**

Users testing code that uses Resilion need test doubles and assertion helpers. Currently `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` covers the primary scenario, but a dedicated package could provide:
- `TestPipeline` / `TestPipeline<T>` that record all executions
- Circuit breaker state assertion helpers
- Deterministic event capture
- Pre-configured pipelines for common test scenarios

**Decision:** Defer until real usage patterns emerge from production users.

---

### 12. Hedging: HedgingDelayGenerator

**Type:** Feature — Strategy Enhancement

**Why**

The current `HedgingDelay` is a static `TimeSpan` — the same delay for every hedged attempt. Some scenarios benefit from dynamic delays (e.g., shorter delays for later attempts, or delays computed from recent latency percentiles).

**Design**

Add `Func<HedgingDelayGeneratorArgs, TimeSpan>? HedgingDelayGenerator` to `HedgingStrategyOptions<T>`. When set, `HedgingDelay` is ignored for that attempt.

```csharp
public readonly record struct HedgingDelayGeneratorArgs(int AttemptNumber, ResilienceContext Context);
```

**Complexity:** Low. Thread the generator call into the delay logic in the for-loop.

---

### 14. Hedging: HedgingRejectedException with Aggregated Errors

**Type:** Improvement — Error Reporting

**Why**

`HedgingRejectedException` is defined but never thrown. When all hedging attempts fail, the implementation currently returns the last failure's exception directly via `Outcome`. This means the user only sees the last attempt's exception, losing context about earlier failures.

**Design**

When all attempts fail, collect all attempt exceptions and either:
- Throw `HedgingRejectedException` with `AttemptExceptions` containing all failures, or
- Return an `Outcome` with the primary (first) attempt's exception but attach others via `AggregateException`

The first option changes behavior for users who `catch (InvalidOperationException)` today — their catch would stop matching. The second is backward-compatible.

**Decision:** Defer. The current behavior (last exception propagates) is simple and predictable. Revisit if users need aggregated error reporting.

---

## Known Tradeoffs (Documented, Not Planned to Fix)

### Null implicit conversion creates "successful" null outcome

`Outcome<string> o = (string)null;` creates a success outcome with a null result. This is consistent with how `Task<string>` handles null — `Task.FromResult<string>(null)` is a valid completed task. For nullable reference types this is expected behavior. Users working with non-nullable value types cannot trigger this.

**Location:** [Outcome.cs](../src/Resilion/Outcome.cs) — `implicit operator`

### DelegatingComponent does not dispose composed pipeline resources

When pipelines are composed via `AddPipeline()`, the `DelegatingComponent` intentionally does not dispose the inner component. This is because the inner pipeline may be shared — the same pre-built `Pipeline` can be flattened into multiple builders. The caller owns the lifetime of composed pipelines and must dispose them separately.

**Location:** [PipelineBuilder.cs](../src/Resilion/PipelineBuilder.cs) — `DelegatingComponent.Dispose()`

### ResilienceContextPool cap is approximate

The pool size check (`_pool.Count < MaxPoolSize`) followed by `_pool.Add(context)` is a TOCTOU race — multiple threads can all pass the check and add, slightly exceeding the 256 cap. This is intentional. The cap is a heuristic to prevent unbounded memory growth, not a hard limit. Using `Interlocked` or a lock for exact enforcement would add contention on every pool return for no meaningful benefit.

**Location:** [ResilienceContextPool.cs](../src/Resilion/ResilienceContextPool.cs) — `Return()`

### Timeout cancellation classification has a narrow race window

In `TimeoutStrategy.WasCancelledByTimeout`, there is a TOCTOU window between checking `userToken.IsCancellationRequested` and `linkedCts.IsCancellationRequested`. If the user token is cancelled in between, user cancellation may be misclassified as a timeout (producing `TimeoutRejectedException` instead of `OperationCanceledException`). The window is nanoseconds wide and requires exact concurrent timing to trigger. In practice this is not observable.

**Location:** [TimeoutStrategy.cs](../src/Resilion/Timeout/TimeoutStrategy.cs) — `WasCancelledByTimeout()`

### Hedging latency mode calls Task.WhenAny twice

In latency mode, the hedging strategy checks if any attempt completed before the delay by nesting `Task.WhenAny(Task.WhenAny(attempts), delayTask)`. When an attempt completes early, it calls `Task.WhenAny(attempts)` a second time to get the completed task. If a *different* attempt completed between the two calls, the second `WhenAny` may return a different task. This is benign — either task's result is checked, and if it's a failure the hedge launches anyway. But it means the "early success" check is not guaranteed to evaluate the first-completed task's result.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `ExecuteAsync`, lines ~52-65
