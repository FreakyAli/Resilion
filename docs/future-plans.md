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

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they won't be fixed.
