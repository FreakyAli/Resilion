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
| 15 | **DI configurators never applied to registry** | **P0** | Fix | Critical | Low | `AddResiliencePipeline` registers configurators but nothing applies them — users get KeyNotFoundException |
| 16 | **CB FireEvent sync-only on async path** | **P0** | Fix | High | Low | Circuit breaker event handlers always call `.Invoke()` even from `ExecuteAsync` — deadlocks with async handlers under SynchronizationContext |
| 17 | **ResilienceProperties TryGetValue throws on type mismatch** | **P0** | Fix | High | Low | Same string key + different type parameter causes `InvalidCastException` instead of returning false |
| 32 | **Timeout async path missing catch for direct OCE** | **P0** | Fix | High | Low | Inner strategy throws OCE directly (not via Outcome), async path doesn't catch — timeout leaks as raw OCE |
| 33 | **Timeout timer callback can crash process** | **P0** | Fix | Critical | Low | `CTS.Cancel()` in timer callback propagates `AggregateException` from user cancellation callbacks — unhandled on thread pool = process termination |
| 18 | **ManualControl non-atomic initialization** | **P1** | Fix | Medium | Low | `_onReset` set after CAS on `_onIsolate` — race window where Isolate works but Reset throws |
| 19 | **Hedging sync path shares context across attempts** | **P1** | Fix | Medium | Low | Sync `Execute` reuses same ResilienceContext for all sequential attempts — inner strategies see stale Properties |
| 20 | Hedging `ResolveAction` missing `Task.Run` wrapper | P1 | Fix | Medium | Low | Sync path deadlock risk when ActionGenerator returns async action |
| 21 | Registry `GetOrAdd` race leaks undisposed Pipelines | P1 | Fix | Medium | Low | Concurrent first-access creates N pipelines, discards N-1 without Dispose |
| 34 | **Hedging latency-mode: completed task bypasses delay for all subsequent attempts** | **P1** | Fix | High | Medium | Failed task stays in attempts list, `Task.WhenAny` resolves immediately on every subsequent iteration, all hedges launch in burst instead of staggered |
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

> **Completed (removed from backlog):** Solution structure, .editorconfig, global.json, CI workflows, Outcome\<T\>, ResilienceContext pooling, Strategy base types, Pipeline/PipelineBuilder, ResilienceEventHandler union struct, Timeout strategy, Retry strategy (with RetryDelay discriminated union), Circuit Breaker (sliding window, state machine, thread safety), Fallback (with FallbackAction implicit conversions), code review and fixes for 13 issues, Rate Limiter strategy (wrapping System.Threading.RateLimiting), Hedging strategy (parallel/latency/sequential modes, per-attempt CTS, cleanup-on-cancel), Pipeline ordering validation, Telemetry wired into all strategies, Docs (15 files), README, Samples, Benchmarks vs Polly, #6 Pipeline ordering validation, #7 Benchmarks, #8 README and docs, #13 Hedging Properties propagation (CopyFrom added).

---

## P0 — Bugs (fix before any release)

### 15. DI Configurators Never Applied to Registry

**Type:** Fix — Critical correctness bug

**Why**

`AddResiliencePipeline("name", configure)` registers `IPipelineConfigurator` singletons in DI, but nothing ever applies them to the `ResiliencePipelineRegistry<string>`. The registry is registered via `TryAddSingleton` with its parameterless constructor, yielding an empty registry. `BuildRegistry` exists as `internal static` but is only called from test code.

**Impact:** Any user following the DI pattern gets `KeyNotFoundException` at runtime. The entire DI registration surface is non-functional.

**Fix:** Wire `BuildRegistry` into the DI container — either as a factory for the registry singleton, or via `IHostedService` / post-configuration. The simplest fix: replace `TryAddSingleton<ResiliencePipelineRegistry<string>>()` with a factory overload that resolves all `IPipelineConfigurator` instances and applies them.

**Location:** [ResilionServiceCollectionExtensions.cs](../src/Resilion.Extensions/ResilionServiceCollectionExtensions.cs)

---

### 16. Circuit Breaker FireEvent Sync-Only on Async Path

**Type:** Fix — Deadlock under SynchronizationContext

**Why**

`FireEvent` in both `CircuitBreakerStrategy` and `CircuitBreakerTypedStrategy` always calls `handler.Invoke(e)` (sync), even when invoked from `ExecuteAsync`. For async handlers, `Invoke` does `Task.Run(...).GetAwaiter().GetResult()`, blocking the thread. Every other strategy correctly uses `await handler.InvokeAsync(...)` on async paths.

**Impact:** Circuit breaker with async event handler deadlocks under WPF/WinForms SynchronizationContext. Thread pool starvation under load even without SynchronizationContext.

**Fix:** Split `FireEvent` into `FireEvent` (sync) and `FireEventAsync` (async). Call the async variant from `ExecuteAsync`, the sync variant from `Execute`.

**Location:** [CircuitBreakerStrategy.cs](../src/Resilion/CircuitBreaker/CircuitBreakerStrategy.cs), [CircuitBreakerTypedStrategy.cs](../src/Resilion/CircuitBreaker/CircuitBreakerTypedStrategy.cs)

---

### 17. ResilienceProperties TryGetValue Throws on Type Mismatch

**Type:** Fix — Correctness

**Why**

`TryGetValue<TValue>` performs an unchecked cast `(TValue?)raw` on the stored `object?`. The key is the string from `ResiliencePropertyKey<TValue>.Key`, not the type parameter. Two keys with the same string but different types (e.g., `ResiliencePropertyKey<int>("code")` and `ResiliencePropertyKey<string>("code")`) cause `InvalidCastException` instead of returning `false`.

**Fix:** Wrap the cast in a type check: `if (raw is TValue typed) { value = typed; return true; }`.

**Location:** [ResilienceProperties.cs](../src/Resilion/ResilienceProperties.cs) — `TryGetValue`

---

### 32. Timeout Async Path Missing Catch for Direct OCE

**Type:** Fix — Correctness

**Why**

The async `ExecuteAsync` in `TimeoutStrategy` only checks `outcome.Exception is OperationCanceledException` (line ~45). But if the inner callback throws `OperationCanceledException` directly (not captured in an Outcome — e.g., `Task.Delay` throws when its token cancels during Retry delay), there is no `catch (OperationCanceledException)` block on the async path. The sync path has one (line ~105). The raw OCE propagates to the caller instead of being wrapped in `TimeoutRejectedException`.

**Impact:** Users can't distinguish timeout from external cancellation when Timeout wraps Retry and the timeout fires during a retry delay.

**Fix:** Add `catch (OperationCanceledException oce) when (WasCancelledByTimeout(linkedCts, previousToken))` to the async path's try block, matching the sync path.

**Location:** [TimeoutStrategy.cs](../src/Resilion/Timeout/TimeoutStrategy.cs) — `ExecuteAsync`

---

### 33. Timeout Timer Callback Can Crash Process

**Type:** Fix — Critical

**Why**

The timeout timer callback is `static state => ((CancellationTokenSource)state!).Cancel()`. `CancellationTokenSource.Cancel()` invokes all registered cancellation callbacks. If any callback throws, `Cancel()` wraps the exception in `AggregateException` and rethrows. Since this runs on a thread pool timer thread with no try-catch, the unhandled exception terminates the process in .NET 6+.

**Impact:** A user strategy that registers a faulty cancellation callback (e.g., `token.Register(() => resource.Dispose())` where Dispose throws) crashes the entire application when a timeout fires.

**Fix:** Wrap the timer callback: `static state => { try { ((CancellationTokenSource)state!).Cancel(); } catch { /* log or swallow */ } }`.

**Location:** [TimeoutStrategy.cs](../src/Resilion/Timeout/TimeoutStrategy.cs) — `CreateTimer` callback, lines ~36 and ~86

---

## P1 — Do First

### 18. ManualControl Non-Atomic Initialization

**Type:** Fix — Race condition

**Why**

`CircuitBreakerManualControl.Initialize` uses `Interlocked.CompareExchange` on `_onIsolate`, then plain assignment on `_onReset`. Between the CAS and the assignment, a concurrent `IsolateAsync()` succeeds but `ResetAsync()` throws — leaving the circuit permanently isolated.

**Fix:** Set both fields atomically. Options: use a lock, or pack both into a single `(Func<Task>, Func<Task>)` tuple and CAS that.

**Location:** [CircuitBreakerManualControl.cs](../src/Resilion/CircuitBreaker/CircuitBreakerManualControl.cs) — `Initialize`

---

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

### 34. Hedging Latency-Mode: Completed Task Bypasses Delay for All Subsequent Attempts

**Type:** Fix — Behavioral incorrectness

**Why**

In latency mode (`HedgingDelay > 0`), once an attempt completes with a handled failure, it stays in `attempts`. On the next loop iteration, `Task.WhenAny(attempts)` resolves immediately on the already-completed task, so the hedging delay is never honored. All remaining hedged attempts launch in a burst instead of being staggered by `HedgingDelay`.

**Example:** `HedgingDelay = 2s`, `MaxHedgedAttempts = 4`. Primary fails in 100ms. Attempts 1, 2, 3 all launch within milliseconds instead of being spaced 2s apart.

**Fix:** Remove completed tasks from `attempts` after checking their outcome in the latency-mode early-check block.

**Location:** [HedgingStrategy.cs](../src/Resilion/Hedging/HedgingStrategy.cs) — `ExecuteAsync`, latency mode block

---

### 21. Registry GetOrAdd Race Leaks Undisposed Pipelines

**Type:** Fix — Resource leak

**Why**

`ConcurrentDictionary.GetOrAdd` with a factory may execute the factory multiple times under concurrent access. Only one `Pipeline` is stored; the rest are discarded without `Dispose`. Custom strategies holding unmanaged resources would leak.

**Fix:** Use `Lazy<Pipeline>` as the dictionary value, or use a lock per key to ensure single creation.

**Location:** [ResiliencePipelineRegistry.cs](../src/Resilion.Extensions/ResiliencePipelineRegistry.cs) — `GetPipeline`

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
