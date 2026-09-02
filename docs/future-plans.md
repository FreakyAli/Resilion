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
| 19 | **Hedging sync path shares context across attempts** | **P1** | Fix | Medium | Low | Sync `Execute` reuses same ResilienceContext for all sequential attempts — inner strategies see stale Properties |
| 20 | Hedging `ResolveAction` missing `Task.Run` wrapper | P1 | Fix | Medium | Low | Sync path deadlock risk when ActionGenerator returns async action |
| 22 | Hedging latency-mode: Double WhenAny + stale task | P2 | Fix | Medium | Medium | Completed task stays in list, second WhenAny may return wrong task, wastes cycles |
| 23 | Interlocked.Increment under lock in SlidingWindow | P2 | Fix | Low | Low | Wasted memory barrier — plain `++` under lock is correct and cheaper |
| 24 | Double lock acquisition per CB Closed-state call | P2 | Fix | Medium | Low | RecordSuccess + GetFailureRatio each acquire SlidingWindow lock separately |
| 25 | ConcurrentBag.Count in pool Return | P2 | Fix | Medium | Low | `Count` enumerates all thread-local lists — use Interlocked counter |
| 26 | CB typed/non-generic strategy duplication (~200 lines) | P2 | Improvement | Medium | Medium | Extract shared state machine class |
| 27 | Retry typed/non-generic strategy duplication (~100 lines) | P2 | Improvement | Medium | Medium | Extract shared retry loop |
| 28 | PipelineBuilder typed/non-generic duplication (~80 lines) | P2 | Improvement | Medium | Medium | Extract shared base class |
| 29 | ShouldHandleOutcome copy-pasted across 4 options classes | P2 | Improvement | Low | Low | Extract to shared static helper |
| 4 | Per-call delegate allocation in pipeline chain | P2 | Fix | Medium | High | Middleware pattern inherent cost |
| 5 | CancellationTokenSource pooling for Timeout/Hedging | P2 | Improvement | Medium | Medium | Reduce alloc on hot path |
| 11 | Hedging: Task.WhenAny memory leak | P2 | Fix | Medium | Medium | Continuations not deregistered on losing tasks |
| 9 | WaitHandle allocation in sync retry | P3 | Fix | Low | Low | CancellationToken.WaitHandle lazy alloc |
| 10 | Resilion.Testing package | P3 | Feature | Medium | Low | Test doubles, assertion helpers |
| 12 | Hedging: HedgingDelayGenerator | P3 | Feature | Low | Low | Dynamic delay per attempt |
| 14 | Hedging: HedgingRejectedException with aggregated errors | P3 | Improvement | Low | Low | Currently dead code — never thrown |
| 30 | FallbackAction/ResilienceEventHandler Task.Run missing CancellationToken | P3 | Fix | Low | Low | Sync-over-async wrappers can't be interrupted by cancellation |
| 31 | Duplicate XML doc on ResilienceProperties.Clear | P3 | Fix | Low | Low | Copy-paste leftover — orphaned summary block |

> **Completed (removed from backlog):** Solution structure, .editorconfig, global.json, CI workflows, Outcome\<T\>, ResilienceContext pooling, Strategy base types, Pipeline/PipelineBuilder, ResilienceEventHandler union struct, Timeout strategy, Retry strategy (with RetryDelay discriminated union), Circuit Breaker (sliding window, state machine, thread safety), Fallback (with FallbackAction implicit conversions), code review and fixes for 13 issues, Rate Limiter strategy (wrapping System.Threading.RateLimiting), Hedging strategy (parallel/latency/sequential modes, per-attempt CTS, cleanup-on-cancel), Pipeline ordering validation, Telemetry wired into all strategies, Docs (15 files), README, Samples, Benchmarks vs Polly, #6 Pipeline ordering validation, #7 Benchmarks, #8 README and docs, #13 Hedging Properties propagation (CopyFrom added), #15 DI configurators, #16 CB deadlock, #17 ResilienceProperties type safety, #32 Timeout OCE catch, #33 Timeout timer callback crash, #18 ManualControl initialization, #21 Registry race, #34 Hedging stale task.

---

## P1 — Do First

### 19. Hedging Sync Path Shares Context Across Attempts

**Type:** Fix — Correctness

**Why**

Sync `Execute` in `HedgingStrategy` passes the same `ResilienceContext` to all sequential attempts. Any inner strategy that writes to `context.Properties` during attempt N leaves stale state visible to attempt N+1. The async path correctly creates a fresh per-attempt context.

**Fix:** Create a per-attempt context copy in the sync path, matching the async path's `ResilienceContextPool.Shared.Rent(ct)` + `Properties.CopyFrom()`.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `Execute`, line ~119

---

### 20. Hedging ResolveAction Missing Task.Run Wrapper

**Type:** Fix — Deadlock risk

**Why**

Sync `ResolveAction` wraps a custom `ActionGenerator`'s async result with `.GetAwaiter().GetResult()` directly, without `Task.Run`. Every other sync-over-async site in the codebase uses `Task.Run` to escape the SynchronizationContext.

**Fix:** Match the pattern: `Task.Run(() => customAction(ctx.CancellationToken).AsTask()).GetAwaiter().GetResult()`.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `ResolveAction`, line ~241

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


### 11. Hedging: Task.WhenAny Memory Leak

**Type:** Fix — Performance/Correctness

**Why**

`WaitForBestOutcome` uses `Task.WhenAny(remaining)` in a loop, removing completed tasks from the list. The standard `Task.WhenAny` implementation registers a continuation on every task in the list. When a task completes, the continuations on the *other* tasks are not deregistered — they hold references to the `Task.WhenAny` result task and its captured state, preventing garbage collection until all tasks in the list complete.

In a pipeline with 3+ hedged attempts where the first wins quickly, the losing tasks' continuations hold memory until they eventually complete or are cancelled.

**Potential Fix**

Implement a custom `FirstCompletedAsync` that uses `TaskCompletionSource` + per-task continuations that deregister themselves on completion. Or use `WaitAsync` chaining.

**Impact:** Medium. Only affects memory under hedging with many concurrent attempts. The cleanup in `finally` (awaiting all tasks) limits the leak duration.

---

### 22. Hedging Latency-Mode Double WhenAny + Stale Task

**Type:** Fix — Correctness (minor)

**Why**

In latency mode, when an attempt completes before the delay, `Task.WhenAny(attempts)` is called twice (once nested, once after). The second call may return a different task. Also, the completed/failed task stays in `attempts`, so `WaitForBestOutcome` re-evaluates it, wasting a cycle and potentially setting `lastFailure` to the wrong attempt.

**Fix:** Store the result of the first `WhenAny`, remove completed tasks from the list before launching the next attempt.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `ExecuteAsync`, latency mode block

---

### 23. Interlocked.Increment Under Lock in SlidingWindow

**Type:** Fix — Efficiency

**Why**

`RecordSuccess()` and `RecordFailure()` acquire `_lock` then use `Interlocked.Increment`. Since the lock is held, `Interlocked` adds a full memory barrier for zero benefit. Plain `++` is correct and cheaper.

**Fix:** Replace `Interlocked.Increment(ref _buckets[_currentBucketIndex].Successes)` with `_buckets[_currentBucketIndex].Successes++`.

**Location:** [SlidingWindow.cs](../src/Resilion/CircuitBreaker/SlidingWindow.cs) — `RecordSuccess`, `RecordFailure`

---

### 24. Double Lock Acquisition Per CB Closed-State Call

**Type:** Fix — Efficiency

**Why**

In Circuit Breaker's Closed path, `RecordAndTransition` calls `_window.RecordFailure()` then `_window.GetFailureRatio()`. Each acquires `SlidingWindow._lock` independently. Both also run `AdvanceWindow()`. This doubles lock contention and duplicates window-advancement.

**Fix:** Add `RecordAndGetRatio(bool isFailure, out int totalCount)` to SlidingWindow that does record + ratio under one lock acquisition.

**Location:** [CircuitBreakerStrategy.cs](../src/Resilion/CircuitBreaker/CircuitBreakerStrategy.cs), [SlidingWindow.cs](../src/Resilion/CircuitBreaker/SlidingWindow.cs)

---

### 25. ConcurrentBag.Count in Pool Return

**Type:** Fix — Efficiency

**Why**

`ResilienceContextPool.Return` checks `_pool.Count < MaxPoolSize` before adding. `ConcurrentBag.Count` is O(n), acquiring locks on all thread-local lists. With many threads, this serializes the return path.

**Fix:** Track count with `Interlocked.Increment`/`Decrement` on an `int _count` field instead.

**Location:** [ResilienceContextPool.cs](../src/Resilion/ResilienceContextPool.cs) — `Return`

---

### 26–29. Strategy/Builder/Options Duplication

**Type:** Improvement — Maintainability

**Why**

Code review identified significant duplication between typed and non-generic variants:
- **#26:** `CircuitBreakerStrategy` / `CircuitBreakerTypedStrategy<T>` — ~200 lines of identical state machine logic
- **#27:** `RetryStrategy` / `RetryStrategy<T>` — ~100 lines of identical retry loop (4 copies: async+sync × typed+untyped)
- **#28:** `PipelineBuilder` / `PipelineBuilder<T>` — ~80 lines of identical Build/EmitWarnings/properties
- **#29:** `ShouldHandleOutcome` — identical method body across `FallbackStrategyOptions<T>`, `RetryStrategyOptions<T>`, `CircuitBreakerStrategyOptions<T>`, `HedgingStrategyOptions<T>`

**Fix:** Extract shared base classes, helper classes, or static methods. For CB, a shared `CircuitBreakerStateMachine` class parameterized on a failure predicate. For Retry, a shared loop parameterized on `Func<Outcome<T>, bool>`. For Builder, a `PipelineBuilderBase`. For ShouldHandle, a static `OutcomePredicates.Default<T>()`.

**Risk:** A bug fix applied to one copy but not the other causes silent behavioral divergence between typed and non-generic variants of the same strategy.

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

### 30. FallbackAction/ResilienceEventHandler Task.Run Missing CancellationToken

**Type:** Fix — Minor correctness

**Why**

Both `FallbackAction<T>.Execute` and `ResilienceEventHandler<T>.Invoke` call `Task.Run(() => ...).GetAwaiter().GetResult()` for sync-over-async without passing `CancellationToken` to `Task.Run`. The blocking call can't be interrupted by cancellation.

**Impact:** Low. Only affects sync path with async handlers/factories, and the underlying async operation would need to observe its own token anyway.

---

### 31. Duplicate XML Doc on ResilienceProperties.Clear

**Type:** Fix — Trivial

**Why**

Two consecutive identical `<summary>Removes all properties.</summary>` blocks before `Clear()`. Copy-paste leftover.

**Location:** [ResilienceProperties.cs](../src/Resilion/ResilienceProperties.cs)

---

---

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they won't be fixed.
