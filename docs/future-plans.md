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
| ~~14~~ | ~~Hedging: HedgingRejectedException with aggregated errors~~ | — | — | — | — | **Removed** — dead code deleted; revisit as opt-in flag post-v1.0 |
| 30 | FallbackAction/ResilienceEventHandler Task.Run missing CancellationToken | P3 | Fix | Low | Low | Sync-over-async wrappers can't be interrupted by cancellation |
| 31 | Strict ordering validation mode for PipelineBuilder | P1 | Improvement | High | Low | Throw on dangerous misorderings (CB outside Retry, Fallback not outermost) |
| 32 | Warn on silent typed strategy type-mismatch | P2 | Improvement | Medium | Low | TypedStrategyComponent silently skips on type mismatch — add Debug warning |
| 33 | IAsyncDisposable on Pipeline and Strategy | P2 | Improvement | Medium | Medium | Enable `await using`; async-disposable resources in strategies |
| 34 | XML doc warning on Outcome&lt;T&gt; implicit operator | P1 | Documentation | Low | Low | Outcome&lt;Exception&gt; implicit conversion creates success wrapping an exception |
| 35 | Guard PipelineBuilder against post-Build() usage | P1 | Fix | Medium | Low | Throw if AddStrategy() called after Build() — prevents silent misuse |
| 36 | Verify or remove AOT compatibility claim | P1 | Infrastructure | Medium | Medium | IsAotCompatible=true but no AOT publish test in CI |
| 37 | Hedging sync path: throw on non-sequential mode | P1 | Fix | High | Low | Sync Execute() silently degrades parallel/latency hedging to sequential |
| 38 | "Why Resilion over Polly?" comparison page | P1 | Documentation | High | Low | Honest comparison — where Resilion wins, where Polly wins, roadmap to close gap |
| 39 | Resilion.Http — HttpClient integration | P1 | Feature | Critical | High | AddStandardResilienceHandler() for IHttpClientFactory. #1 missing feature for adoption |
| 40 | Resilion.Chaos — Chaos engineering | P2 | Feature | Medium | High | Fault/outcome/latency/behavior injection (Simmy equivalent) |
| 41 | Pipeline dynamic reload via IOptionsMonitor | P2 | Feature | Medium | High | Auto-recreate pipeline when bound options change |
| 42 | Keyed services support | P2 | Feature | Medium | Medium | [FromKeyedServices("key")] for direct DI injection (.NET 8+) |
| 43 | PredicateBuilder&lt;T&gt; fluent API | P3 | Feature | Low | Medium | .Handle&lt;TException&gt;().HandleResult(r => ...) with implicit conversion |
| 44 | IConfiguration binding for strategy options | P3 | Feature | Low | Medium | Strategy options from appsettings.json |
| 45 | Telemetry enrichment (MeteringEnricher, listeners) | P3 | Feature | Low | Medium | Custom tags, raw event listeners, severity control |

---

## P2 — Do Next / Post-v1.0

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

### ~~14. Hedging: HedgingRejectedException with Aggregated Errors~~ — REMOVED

`HedgingRejectedException` was dead code (defined but never thrown). It has been deleted. If aggregated error reporting is needed post-v1.0, re-introduce as an opt-in flag on `HedgingStrategyOptions<T>` so existing catch behavior isn't broken.

---

### 30. FallbackAction/ResilienceEventHandler Task.Run Missing CancellationToken

**Type:** Fix — Minor correctness

**Why**

Both `FallbackAction<T>.Execute` and `ResilienceEventHandler<T>.Invoke` call `Task.Run(() => ...).GetAwaiter().GetResult()` for sync-over-async without passing `CancellationToken` to `Task.Run`. The blocking call can't be interrupted by cancellation.

**Impact:** Low. Only affects sync path with async handlers/factories, and the underlying async operation would need to observe its own token anyway.

---

## P1 — Pre-v1.0 or Shortly After

### 31. Strict Ordering Validation Mode for PipelineBuilder

**Type:** Improvement — Safety

**Why**

The ordering validator currently emits warnings via `Debug.WriteLine` for dangerous strategy misorderings (e.g., CircuitBreaker outside Retry — causes the CB to see retried failures and trip prematurely). In production, nobody reads Debug output. This is a silent footgun.

**Design**

Split ordering issues in `OrderingValidator` into two categories: **errors** (CB outside Retry, Fallback not outermost — these are always wrong) and **warnings** (3+ timeouts, Hedging+Retry together — these are situationally wrong). Add a `ThrowOnOrderingErrors` property to both builders, defaulting to `true`. When true, errors throw `InvalidOperationException` at `Build()` time. Warnings still go to the Debug/callback path. Users can opt out with `ThrowOnOrderingErrors = false`.

---

### 34. XML Doc Warning on Outcome&lt;T&gt; Implicit Operator

**Type:** Documentation

**Why**

There's an implicit conversion from `TResult` to `Outcome<TResult>` (success). This means `Outcome<Exception>` implicitly converts an Exception into a *success* outcome wrapping that exception — not a failure. This is acknowledged in `tradeoffs.md` but the code itself gives no warning at the point of use.

**Fix:** Add XML doc `<remarks>` on the implicit operator warning that `Pipeline<Exception>` users should use `Outcome.FromResult()` / `Outcome.FromException()` explicitly to avoid ambiguity. Documentation-only change for v1.0.

---

### 35. Guard PipelineBuilder Against Post-Build() Usage

**Type:** Fix — API Safety

**Why**

The XML docs say "The builder should not be used after calling Build()" but nothing enforces it. Calling `AddRetry()` after `Build()` silently accumulates strategies for a pipeline that was already constructed. The builder doesn't error and the new strategies are lost.

**Fix:** Add a `_built` boolean field to `PipelineBuilder` (and `PipelineBuilder<T>`, or the shared base class). Set to `true` at the start of `Build()`. Check it at the start of every `AddStrategy()`, `AddPipeline()`, and delegate strategy overload — throw `InvalidOperationException("This builder has already been built. Create a new builder for a new pipeline.")`.

---

### 36. Verify or Remove AOT Compatibility Claim

**Type:** Infrastructure

**Why**

`src/Directory.Build.props` sets `IsAotCompatible=true` but there's no AOT publish test or trim analysis in CI. The `Unsafe.As` usage in `TypedStrategyComponent` and the generic method patterns may produce trim warnings or runtime failures under AOT. This is an unvalidated claim.

**Fix:** Add a CI step (or local test script) that runs `dotnet publish -r <rid> /p:PublishAot=true` on the sample app or a minimal test project. If it produces trim warnings or fails, either fix the trim issues (add `[DynamicallyAccessedMembers]` attributes, suppress false positives) or remove `IsAotCompatible=true` from `Directory.Build.props`. An unverified claim is worse than no claim.

---

### 37. Hedging Sync Path: Throw on Non-Sequential Mode

**Type:** Fix — Behavioral Correctness

**Why**

Hedging's entire value proposition is parallel/concurrent execution. But the sync `Execute()` path always runs sequentially regardless of `HedgingDelay`. A developer using `HedgingDelay = TimeSpan.Zero` (parallel mode) with sync execution gets sequential behavior with zero indication that parallel mode was silently ignored.

**Fix:** At the top of `HedgingStrategy<T>.Execute()`, if `_options.HedgingDelay != Timeout.InfiniteTimeSpan`, throw `InvalidOperationException("Parallel and latency hedging modes require async execution. Use ExecuteAsync(), or set HedgingDelay to Timeout.InfiniteTimeSpan for sequential mode.")`. This is better than silent degradation — developers need to know their hedging isn't actually hedging.

---

### 38. "Why Resilion Over Polly?" Comparison Page

**Type:** Documentation — Adoption

**Why**

The README says "Why Resilion?" but doesn't address the elephant in the room: why pick this over Polly, which has years of production use and a massive ecosystem? Developers evaluating alternatives need an honest comparison to make an informed choice.

**Design**

Create `docs/comparison-with-polly.md` with three sections:

1. **Where Resilion is better**: Zero deps in core, true sync execution (not sync-over-async), `RetryDelay` discriminated union, sync/async callback ergonomics, simpler API surface, always free
2. **Where Polly is better (today)**: HttpClient integration, chaos engineering (Simmy), dynamic reload, testing package, PredicateBuilder fluent API, years of battle-testing, massive ecosystem
3. **Roadmap to close the gap**: Link to the post-v1.0 items (39–45) in this document

Tone: "Polly is great software that moved the .NET ecosystem forward. We built Resilion because we believe resilience should be free, with a simpler API, and without pulling in dependencies you didn't ask for."

---

### 39. Resilion.Http — HttpClient Integration

**Type:** Feature — Critical for Adoption

**Why**

The most common use case for a resilience library is wrapping `HttpClient` calls. Polly's `Microsoft.Extensions.Http.Resilience` package provides `AddStandardResilienceHandler()` which adds a pre-configured 5-strategy pipeline to an `HttpClient` via `IHttpClientFactory`. Without this, Resilion misses the #1 onboarding path.

**Design**

New package `Resilion.Http` with:
- `AddStandardResilienceHandler()` extension on `IHttpClientBuilder` — pre-configured pipeline: RateLimiter → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout, with defaults matching Polly's (retry on 5xx, 429, 408, `HttpRequestException`, `TimeoutRejectedException`)
- `AddStandardHedgingHandler()` — replaces retry with hedging for latency-sensitive scenarios
- `AddResilienceHandler(key, builder => ...)` — custom pipeline on a named HttpClient

Implementation: `DelegatingHandler` wrapping the Resilion pipeline. References `Resilion.Extensions` and `Microsoft.Extensions.Http`.

---

### 32. Warn on Silent Typed Strategy Type-Mismatch

**Type:** Improvement — Developer Experience

**Why**

When a typed strategy (e.g., `FallbackStrategy<string>`) is added to an untyped `Pipeline` and executed with a different result type (`ExecuteAsync<int>`), the `TypedStrategyComponent` silently skips the strategy because `typeof(int) != typeof(string)`. No error, no warning — the strategy just doesn't run.

**Fix:** In `TypedStrategyComponent`'s execute methods, when the type check fails and the strategy is skipped, emit a `Debug.WriteLine` warning with the mismatched types. Minimal fix, no behavioral change, but gives visibility when debugging.

---

### 33. IAsyncDisposable on Pipeline and Strategy

**Type:** Improvement — Modern .NET Patterns

**Why**

`Pipeline` and `Pipeline<T>` implement `IDisposable` but not `IAsyncDisposable`. This means `await using` doesn't work. If any strategy (current or future) holds async-disposable resources, `Dispose()` blocks or skips async cleanup.

**Fix:** Add `IAsyncDisposable` to `Pipeline`, `Pipeline<T>`, `Strategy`, `Strategy<TResult>`, and `PipelineComponent`. The `DisposeAsync()` method walks the component chain asynchronously. The existing sync `Dispose()` stays as-is for backward compat.

---

### 40. Resilion.Chaos — Chaos Engineering

**Type:** Feature

**Why**

Polly v8.3+ includes Simmy for chaos engineering — fault injection, outcome injection, latency injection, and behavior injection. This is table stakes for production resilience testing.

**Design**

New package `Resilion.Chaos` with four strategies: `AddChaosFault`, `AddChaosOutcome`, `AddChaosLatency`, `AddChaosBehavior`. Common options: `InjectionRate` (0–1), `InjectionRateGenerator` (dynamic), `Enabled` (bool), `EnabledGenerator` (dynamic enable/disable). Generators take precedence over static values. Chaos strategies go last in the pipeline so outer resilience strategies react to injected chaos.

---

### 41. Pipeline Dynamic Reload via IOptionsMonitor

**Type:** Feature

**Why**

Polly supports `context.EnableReloads<TOptions>()` which auto-recreates pipelines when `IOptionsMonitor<T>` detects a configuration change. Without this, changing retry counts or timeout durations requires an app restart.

**Design**

`ResiliencePipelineRegistry` gains `IChangeToken` support. When a watched `IOptionsMonitor<T>` fires, the registry invalidates the cached `Lazy<Pipeline>` for the affected key, creates a new one from the updated options, and atomically swaps it. In-flight executions on the old pipeline complete normally; new calls get the new pipeline. `OnPipelineDisposed` callback fires after the old pipeline has no outstanding executions.

---

### 42. Keyed Services Support

**Type:** Feature

**Why**

.NET 8 introduced keyed DI services (`[FromKeyedServices("key")]`). Polly v8.3+ supports this for direct injection of named pipelines without going through the registry.

**Fix:** Register each named pipeline as a keyed singleton alongside the registry entry. Consumers can inject `[FromKeyedServices("http-api")] Pipeline pipeline` directly.

---

### 43. PredicateBuilder&lt;T&gt; Fluent API

**Type:** Feature — API Ergonomics

**Why**

Polly provides `new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().HandleResult(r => r.StatusCode == 500)` with implicit conversion to `Func<Outcome<T>, bool>`. Resilion requires writing the full predicate lambda manually. For simple cases this is fine, but complex predicates get verbose.

**Fix:** Add a `PredicateBuilder<T>` class with `.Handle<TException>()`, `.HandleResult(Func<T, bool>)`, and implicit conversion to `Func<Outcome<T>, bool>`. Low priority — the current lambda approach works and is more explicit.

---

### 44. IConfiguration Binding for Strategy Options

**Type:** Feature

**Why**

Enable binding strategy options from `appsettings.json` so timeout durations, retry counts, and other parameters can be changed without recompiling. Requires #41 (dynamic reload) to be useful — without reload, config changes require app restart.

---

### 45. Telemetry Enrichment

**Type:** Feature

**Why**

Polly supports `MeteringEnricher` (custom tags on all telemetry events), `TelemetryListeners` (raw event listeners for custom consumers), and `SeverityProvider` (adjust/suppress event severity). Resilion's current telemetry is static counters only.

**Fix:** Add extensibility points in `Resilion.Extensions` for custom meter tags and raw event subscription. Low priority unless enterprise users need it.

---

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they won't be fixed.
