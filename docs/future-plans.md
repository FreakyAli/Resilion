# Future Plans

Features, improvements, and known tradeoffs still open for future versions of Resilion. This
is a todo list, not a changelog — once something here is implemented, it comes out of this file
(see [CHANGELOG.md](../CHANGELOG.md) for what's already shipped). Each section includes enough
detail to serve as a starting point for implementation.

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
| 39 | Resilion.Http — HttpClient integration | P1 | Feature | Critical | High | AddStandardResilienceHandler() for IHttpClientFactory. #1 missing feature for adoption |
| 49 | Telemetry counters carry no dimensions/tags | P1 | Fix | Critical | Medium | Add pipeline.name, operation.key tags to all counter increments |
| 50 | ActivitySource is declared but never used | P1 | Fix | Medium | Medium | Wrap strategy execution in StartActivity() or remove promise |
| 51 | No public API surface tracking | P1 | Infrastructure | Critical | Low | PublicApiAnalyzers, PublicAPI.Shipped.txt, CI enforcement |
| 4 | Per-call delegate allocation in pipeline chain | P2 | Fix | Medium | High | Middleware pattern inherent cost |
| 5 | CancellationTokenSource pooling for Timeout/Hedging | P2 | Improvement | Medium | Medium | Reduce alloc on hot path |
| 11 | Hedging: Task.WhenAny memory leak | P2 | Fix | Medium | Medium | Continuations not deregistered on losing tasks |
| 27 | Retry typed/non-generic strategy duplication (~100 lines) | P2 | Improvement | Medium | Medium | Extract shared retry loop |
| 40 | Resilion.Chaos — Chaos engineering | P2 | Feature | Medium | High | Fault/outcome/latency/behavior injection (Simmy equivalent) |
| 41 | Pipeline dynamic reload via IOptionsMonitor | P2 | Feature | Medium | High | Auto-recreate pipeline when bound options change |
| 42 | Keyed services support | P2 | Feature | Medium | Medium | [FromKeyedServices("key")] for direct DI injection (.NET 8+) |
| 46 | Timeout cancellation TOCTOU race fix | P2 | Fix | Low | Low | Eliminate narrow race in WasCancelledByTimeout — moved from tradeoffs.md |
| 52 | No SourceLink, symbol packages, or deterministic builds | P2 | Infrastructure | Medium | Low | PublishRepositoryUrl, IncludeSymbols, SymbolPackageFormat, ContinuousIntegrationBuild |
| 9 | WaitHandle allocation in sync retry | P3 | Fix | Low | Low | CancellationToken.WaitHandle lazy alloc |
| 10 | Resilion.Testing package | P3 | Feature | Medium | Low | Test doubles, assertion helpers |
| 12 | Hedging: HedgingDelayGenerator | P3 | Feature | Low | Low | Dynamic delay per attempt |
| 30 | FallbackAction/ResilienceEventHandler Task.Run missing CancellationToken | P3 | Fix | Low | Low | Sync-over-async wrappers can't be interrupted by cancellation |
| 43 | PredicateBuilder&lt;T&gt; fluent API | P3 | Feature | Low | Medium | .Handle&lt;TException&gt;().HandleResult(r => ...) with implicit conversion |
| 44 | IConfiguration binding for strategy options | P3 | Feature | Low | Medium | Strategy options from appsettings.json |
| 45 | Telemetry enrichment (MeteringEnricher, listeners) | P3 | Feature | Low | Medium | Custom tags, raw event listeners, severity control |
| 47 | CI code coverage collection | P3 | Infrastructure | Low | Medium | Blocked — verified incompatibility, see item #47 below |
| 48 | Benchmark CI on PRs | P3 | Infrastructure | Low | Low | Catch perf regressions automatically instead of only on manual runs |
| 53 | No SECURITY.md | P3 | Documentation | Low | Low | Vulnerability disclosure policy for OSS infrastructure library |
| 54 | Evaluate multi-targeting net9.0 for System.Threading.Lock | P3 | Improvement | Low | Medium | Benchmark vs object lock, conditional compile if faster |

---

## P1 — Pre-v1.0 or Shortly After

### ✅ 49. Telemetry Counters Carry No Dimensions/Tags — IMPLEMENTED

**Status:** Complete  
**What was done:**
1. Added `pipeline.name` and `operation.key` tag constants to `ResilionTelemetry`
2. Extended `ResilienceContext` with `PipelineName` property
3. Modified `Pipeline` and `Pipeline<TResult>` to store and pass pipeline name through context
4. Updated `PipelineBuilder.Build()` to pass name to Pipeline constructor
5. Updated `ResiliencePipelineRegistry` to set builder.Name from the pipeline key
6. Updated all counter.Add() calls in 6 strategy files to include tags:
   - RetryStrategy.cs (4 locations)
   - CircuitBreakerStateMachine.cs (2 locations)
   - TimeoutStrategy.cs (2 locations)
   - FallbackStrategy.cs (2 locations)
   - HedgingStrategy.cs (1 location)
   - RateLimiterStrategy.cs (2 locations)

**Files modified:** 11 core files + bug fixes  
**Verification:** All counters now emit with pipeline.name and operation.key tags

---

### ✅ 50. ActivitySource Is Declared But Never Used — IMPLEMENTED

**Status:** Complete  
**What was done:**
1. Added `using System.Diagnostics` to 6 strategy files
2. Wrapped strategy execution in `ActivitySource.StartActivity()` calls
3. Set tags for strategy name, pipeline name, and operation key on each activity
4. Tagged outcomes with strategy-specific values (success, failure, timeout, rejected, etc.)
5. Implemented spans in:
   - RetryStrategy (both async and sync)
   - TimeoutStrategy (both async and sync)
   - FallbackStrategy (both async and sync)
   - HedgingStrategy (async path)
   - CircuitBreakerStrategy (both async and sync)
   - RateLimiterStrategy (both async and sync)

**Files modified:** 6 strategy files  
**Verification:** Spans now emitted with distributed tracing support enabled

---

### ✅ 51. No Public API Surface Tracking — IMPLEMENTED

**Status:** Complete  
**What was done:**
1. Added `Microsoft.CodeAnalysis.PublicApiAnalyzers` (v3.3.4) to `src/Directory.Build.props`
2. Created PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt files for all 3 public packages:
   - Resilion/PublicAPI.Shipped.txt
   - Resilion/PublicAPI.Unshipped.txt
   - Resilion.Extensions/PublicAPI.Shipped.txt
   - Resilion.Extensions/PublicAPI.Unshipped.txt
   - Resilion.RateLimiting/PublicAPI.Shipped.txt
   - Resilion.RateLimiting/PublicAPI.Unshipped.txt
3. Analyzer now enforces that all public APIs must be explicitly tracked
4. CI builds will fail if public surface changes without updating the .Unshipped.txt files

**Files modified:** 1 (Directory.Build.props) + 6 new API tracking files  
**Verification:** Build succeeds, analyzer correctly identifies undocumented public APIs

---

## P1 — Pre-v1.0 or Shortly After

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

**Performance note:** HttpClient pipelines are a hot path in high-throughput services (potentially millions of calls/sec). The per-call delegate allocation in the pipeline chain (#4 below) is acceptable for general use but may start showing up in profiles once this package ships. Benchmark the standard resilience handler under load and revisit #4 if closure allocations become a measurable cost at that scale.

---

### 49. Telemetry Counters Carry No Dimensions/Tags

**Type:** Fix — Correctness

**Why**

Every counter increment in the codebase fires with zero tags. In `RetryStrategy.cs`, `CircuitBreakerStateMachine.cs`, `TimeoutStrategy.cs`, `FallbackStrategy.cs`, `HedgingStrategy.cs`, and `RateLimiterStrategy.cs`, every call to `.Add(1)` on counters like `ResilionTelemetry.RetryAttempts`, `CircuitBreakerStateChanges`, `TimeoutExpirations`, `FallbackActivations`, `HedgingAttempts`, and `RateLimiterRejections` passes zero tags.

In an application with two named pipelines both using Retry, the `resilion.retry.attempts` counter cannot distinguish which pipeline is retrying. The counter becomes nearly useless for observability in multi-pipeline scenarios.

**Fix**

Thread the following tags to every `.Add()` call:
- `pipeline.name` — sourced from `PipelineBuilderBase.Name` (already available at build time, must be passed through `ResilienceContext` at execution time)
- `operation.key` — sourced from `ResilienceContext.OperationKey` (already exists but is never read for telemetry)

Grep confirms zero tag usage across all strategy files. This is a correctness gap in the telemetry contract, not a nice-to-have. Untagged counters in multi-instance deployments are close to useless for debugging and alerting.

**Effort:** Medium. Requires threading pipeline name through context initialization and reading `OperationKey` at each counter site.

---

### 50. ActivitySource Is Declared But Never Used

**Type:** Fix — Correctness

**Why**

`ResilionTelemetry.ActivitySource` is declared as `new ActivitySource("Resilion")` and its XML documentation explicitly instructs consumers to `.AddSource("Resilion")` for distributed tracing support. Grep across all of `src/` finds zero calls to `.StartActivity()` anywhere in the codebase.

This is the same class of problem as the dead `StrategyExecutions` and `StrategyDuration` counters that were already removed from this file — an unverified telemetry promise is worse than no promise. The ActivitySource is technically "used" (publicly declared, referenced in XML docs) but never actually emits a span. Any consumer configuring their tracing stack to listen for "Resilion" will receive nothing.

**Fix**

Either:

1. **Implement spans:** Wrap each strategy's `ExecuteAsync` and `Execute` entry point in a `StartActivity()` call, tagged with strategy name, pipeline name, and outcome (success/failure/cancelled). Wire the resulting `Activity` through to telemetry for correlation. This is the preferred path — spans are valuable for latency profiling and distributed tracing in high-scale systems.

2. **Remove the ActivitySource:** Delete the declaration and the misleading XML docs if tracing is not a v1.0 priority. This is acceptable; the promise can be reinstated when the feature is actually implemented.

The current state (declared but unused) is untenable — it signals a feature that doesn't work.

**Effort:** Medium for implementation, trivial for removal.

---

### 51. No Public API Surface Tracking

**Type:** Infrastructure — Correctness

**Why**

Nothing in this repository guards against accidental breaking changes after v1.0 ships. Confirmed via repo-wide grep: zero matches for "PublicAPI", "ApiCompat", or "PublicApiAnalyzers". No `Microsoft.CodeAnalysis.PublicApiAnalyzers` in dependencies. No `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` files. No `Microsoft.DotNet.ApiCompat` CI check.

Once the API is frozen at v1.0, there is no automated enforcement preventing a later PR from silently changing a public method signature, removing a member, or adding an unintended public API. This is standard practice in shipping libraries (Polly, NUnit, xunit all use PublicApiAnalyzers).

**Fix**

1. Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` NuGet package to `src/Directory.Build.props`.
2. After v1.0 ships, generate `PublicAPI.Shipped.txt` files for each public project per the tool's documentation.
3. Add a CI check in `.github/workflows/` that fails any PR changing public surface without updating the corresponding `.Unshipped.txt` file.

**Effort:** Low. The package is zero-configuration after initial setup. The CI check is a single MSBuild step.

---

## P2 — Do Next / Post-v1.0

### 4. Per-Call Delegate Allocation in Pipeline Chain

**Type:** Fix — Performance

**Why**

In `StrategyComponent.ExecuteAsync`, the lambda `ctx => _next.ExecuteAsync(callback, ctx)` creates a closure capturing `_next` and `callback` on every call. In a pipeline with N strategies, this means N small closure allocations per execution.

**Current State**

This is inherent to the middleware/chain-of-responsibility pattern. Polly v8 has the same cost. The allocations are small (one closure object + one delegate per strategy per call) and negligible compared to real I/O costs. Confirmed empirically in [benchmarks/results](../benchmarks/results/README.md): Resilion's happy-path pipelines allocate a few hundred bytes where Polly's are allocation-free, but Resilion is still faster wall-clock time across every shape benchmarked.

**Potential Fix**

Pre-compose the delegate chain at build time into a single `Func<ResilienceContext, Func<...>, ValueTask<Outcome<T>>>` that doesn't allocate per call. This requires complex generic type threading and may not be feasible without sacrificing API simplicity.

**Decision:** Accept for now. Revisit if profiling on a real high-throughput workload (see #39's performance note) shows it matters.

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

### 27. Retry Typed/Non-Generic Strategy Duplication

**Type:** Improvement — Maintainability

**Why**

`RetryStrategy` / `RetryStrategy<T>` duplicate ~100 lines of identical retry loop (4 copies: async+sync × typed+untyped). The equivalent circuit breaker duplication was already extracted into a shared `CircuitBreakerStateMachine` — that extraction is exactly what let the CB race-condition fix apply to both variants automatically instead of needing to be kept in sync by hand. Retry doesn't have shared mutable state the way CB does, so the risk of silent divergence is lower, but the duplication itself is the same shape of problem.

**Fix**

Extract a shared retry loop parameterized on `Func<Outcome<T>, bool>` for the predicate check, following the same pattern used for the CB state machine.

**Risk:** A bug fix applied to one copy but not the other causes silent behavioral divergence between typed and non-generic variants of the same strategy.

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

### 46. Timeout Cancellation TOCTOU Race Fix

**Type:** Fix — Correctness (moved from [tradeoffs.md](tradeoffs.md))

**Why**

`WasCancelledByTimeout` in `TimeoutStrategy.cs` checks `linkedCts.IsCancellationRequested && !userToken.IsCancellationRequested`. Between the two reads, the user token could become cancelled, causing user cancellation to be misclassified as a timeout (`TimeoutRejectedException` instead of `OperationCanceledException`). The window is nanoseconds wide but it exists, and "we know about the race but chose not to fix it" is a bad look for a library claiming production readiness.

**Fix**

Capture the user token's cancellation state **before** awaiting the callback:

1. Read `bool userWasCancelledBefore = userToken.IsCancellationRequested` before the `await callback(context)` call.
2. After the callback throws `OperationCanceledException`, classify as timeout if: `linkedCts.IsCancellationRequested && !userWasCancelledBefore && !userToken.IsCancellationRequested`.
3. The pre-capture eliminates the race: if the user token was already cancelling before the call, it's always user cancellation. If it wasn't, and the linked CTS fired, it's a timeout.

Alternative (even tighter): register a `CancellationTokenRegistration` on the user token that sets a `volatile bool _userCancelled` flag. Check that flag instead of re-reading the token. Dispose the registration in `finally`. This is ~5 lines and eliminates the race entirely.

**Effort:** Low. The fix is a few lines in `TimeoutStrategy.cs`.

---

### 52. No SourceLink, Symbol Packages, or Deterministic Builds

**Type:** Infrastructure — Developer Experience

**Why**

Confirmed via grep of `src/Directory.Build.props`: zero instances of `PublishRepositoryUrl`, `EmbedUntrackedSources`, `IncludeSymbols`, `SymbolPackageFormat`, or `ContinuousIntegrationBuild` properties anywhere in the build configuration.

Consumers of the Resilion NuGet packages cannot step into Resilion's source code while debugging — SourceLink metadata is missing. No `.snupkg` symbol package is published. This is low-effort but high-trust: Polly, most Microsoft.Extensions libraries, and serious .NET OSS projects enable this by default.

**Fix**

1. Add `Microsoft.SourceLink.GitHub` package reference to `src/Directory.Build.props`.
2. Add the following properties to `src/Directory.Build.props` (or per-project as needed):
   - `<PublishRepositoryUrl>true</PublishRepositoryUrl>`
   - `<EmbedUntrackedSources>true</EmbedUntrackedSources>`
   - `<IncludeSymbols>true</IncludeSymbols>`
   - `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`
3. In the release CI job (`.github/workflows/`), set `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` only during the release build (not in local dev builds — deterministic builds interact poorly with incremental local builds).

**Impact:** Users can now debug into Resilion. Symbol packages enable IDE integration and offline symbol resolution.

**Effort:** Low. These are standard MSBuild properties. No code changes required.

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

### 30. FallbackAction/ResilienceEventHandler Task.Run Missing CancellationToken

**Type:** Fix — Minor correctness

**Why**

Both `FallbackAction<T>.Execute` and `ResilienceEventHandler<T>.Invoke` call `Task.Run(() => ...).GetAwaiter().GetResult()` for sync-over-async without passing `CancellationToken` to `Task.Run`. The blocking call can't be interrupted by cancellation.

**Impact:** Low. Only affects sync path with async handlers/factories, and the underlying async operation would need to observe its own token anyway.

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

### 47. CI Code Coverage Collection

**Type:** Infrastructure

**Why**

The test projects target `Microsoft.Testing.Platform` (MTP) via `xunit.v3`, not classic VSTest. `coverlet.collector` — currently referenced in `tests/Directory.Build.props` — is a VSTest data collector, so `dotnet test --collect:"XPlat Code Coverage"` is silently ignored under MTP: verified locally, it produces zero output and no error. The MTP-native replacement, `Microsoft.Testing.Extensions.CodeCoverage`, does work in isolation (`dotnet test <project> -- --coverage --coverage-output-format cobertura` produces a real `.cobertura.xml`) — but adding it as a shared package reference (`tests/Directory.Build.props`) broke `dotnet build`/`dotnet restore` entirely on this machine's installed SDK (10.0.100, resolved via `global.json`'s `rollForward: latestMajor`) with `NETSDK1013: The TargetFramework value '' was not recognized` — reproduced twice, and confirmed the package is the cause by reverting it and rebuilding successfully. This looks like a version incompatibility between `Microsoft.Testing.Extensions.CodeCoverage` 17.14.2 and the newest installed SDK, not a problem with the approach itself.

**Fix**

Retry with a newer `Microsoft.Testing.Extensions.CodeCoverage` version once one compatible with the SDK(s) this repo actually builds against is confirmed (test in isolation on a throwaway branch before merging into the shared `Directory.Build.props`, since a bad version breaks every project that imports it, not just the one being tested). `coverlet.collector` can then be removed as dead weight. Once collection works, wire `--coverage` into `.github/workflows/test.yml`, upload the `.cobertura.xml` as a build artifact, and only then consider a coverage badge — a badge without a live report behind it is an unverified claim.

**Decision:** Left `coverlet.collector` in place for now (harmless no-op under MTP, at least doesn't break the build) rather than leave the tree in the broken state discovered above.

---

### 48. Benchmark CI on PRs

**Type:** Infrastructure

**Why**

`benchmarks/results/README.md` currently only gets updated by someone manually running the suite and committing the numbers. Nothing catches a PR that accidentally regresses performance between those manual runs.

**Design**

A GitHub Actions workflow that runs `dotnet run -c Release --project benchmarks/Resilion.Benchmarks -- --job short` on PRs touching `src/` or `benchmarks/`, and compares the result against a committed baseline (fail or comment on a regression past some threshold). Not critical for v1.0, but valuable for ongoing development once the library has enough usage that regressions would actually be noticed by users first.

---

### 53. No SECURITY.md

**Type:** Documentation

**Why**

No vulnerability disclosure policy exists (confirmed: `SECURITY.md` not found in repo root). This is a standard, low-effort expectation for OSS infrastructure libraries — it tells users how to privately report security issues instead of disclosing them publicly on GitHub.

**Fix**

Add a `SECURITY.md` in the repository root documenting:
- **Supported versions:** Which versions of Resilion receive security updates (typically N and N-1).
- **Reporting:** Link to GitHub's private security advisory feature (Settings → Security & Analysis → Private vulnerability reporting). Include a brief note that security reports should be filed there instead of public issues.
- **Response SLA:** Expected timeline for acknowledgement and patch release (e.g., "critical issues patched within 7 days").

This is boilerplate text; most large OSS projects have near-identical policies. Low effort, high trust signal.

**Effort:** Low. One short document.

---

### 54. Evaluate Multi-Targeting net9.0 for System.Threading.Lock

**Type:** Improvement — Performance

**Why**

`src/Directory.Build.props` targets `net8.0` only (confirmed). .NET 9 introduced `System.Threading.Lock`, a purpose-built synchronization primitive faster than `lock(object)` under contention, especially in hot-path code with many concurrent waiters.

`CircuitBreakerStateMachine` and `SlidingWindow` both hold a lock on every call while in the Closed state — the most common state in healthy systems. If benchmarks show `System.Threading.Lock` provides a measurable win in that scenario, multi-targeting is justified.

**Fix**

1. **Benchmark first:** Run the existing circuit-breaker-under-load benchmarks with both `lock(object)` and `System.Threading.Lock` implementations in isolation. Only proceed if the new lock type shows >5% wall-clock improvement under the standard load profile.

2. **If justified:** Multi-target `net8.0;net9.0` in `src/Directory.Build.props`. Add conditional compilation in `CircuitBreakerStateMachine.cs` and `SlidingWindow.cs`:
   ```csharp
   #if NET9_0_OR_GREATER
   private System.Threading.Lock _lock = new();
   #else
   private readonly object _lock = new();
   #endif
   ```
   Both APIs are compatible with the same lock syntax; only the initialization changes.

3. **Don't multi-target speculatively.** The cost is a more complex build matrix and CI test surface. Only add net9.0 if the benchmark proves it matters.

**Effort:** Medium. Primarily benchmarking time. The conditional code change is trivial.

---

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they won't be fixed.
