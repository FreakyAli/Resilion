# Future Plans

Features, improvements, and known tradeoffs still open for future versions of Resilion. This
is a todo list, not a changelog — once something here is implemented, it comes out of this file
in the same PR (see [CONTRIBUTING.md](../CONTRIBUTING.md) § Future-plans hygiene for the rule, and
[CHANGELOG.md](../CHANGELOG.md) for what's already shipped). Each entry includes enough detail to
serve as a starting point for implementation.

## Type Classification

| Type | Meaning | Impact | Examples |
|------|---------|--------|----------|
| **Feature** | New capability that extends Resilion's functionality beyond current scope | Additive — users gain new resilience options or integrations | Rate Limiter, Hedging, DI integration, Telemetry |
| **Fix** | Correctness issue, performance bug, or missing validation in existing features | Blocking/Correctness — existing code may behave incorrectly or sub-optimally | Timeout cancellation race, missing telemetry increments |
| **Documentation** | Gaps in docs, missing behavioral explanations, or unclear semantics | Clarity — improves developer experience and correctness of usage | Strategy ordering guide, cancellation semantics |
| **Improvement** | Enhancement to an existing feature that doesn't change its API contract | Quality — existing code works but could work better | Retry loop deduplication, CTS pooling |
| **Infrastructure** | CI/CD, packaging, tooling, or repository quality improvements | Adoption — removes friction for users and contributors | Benchmarks, SourceLink, public API enforcement |

**How to read this file**

- **Every entry carries front-matter** stating its `Status`, `Type`, `Target` release, and the date
  and command last used to **verify** its factual claims. `Status: Complete` is not a legal value —
  a completed item leaves this file.
- **`Target` answers "is this a release blocker?"**; `Priority` answers "how much does it matter?".
  Conflating the two is what let three already-implemented items sit in this file as open work.
- **Fixes** are prioritized over Features when they affect correctness.
- **Documentation** accompanies every Feature — document what it does and why.
- **No claim about current behaviour is written from memory.** Re-run the command from
  [verification.md](verification.md) and record it in the entry's `Verified` field.

## Priority Matrix

Generated from the entries below. A row here without a `### <n>.` entry below — or an
entry without a row here — is a bug in this file; see CONTRIBUTING.md § Future-plans hygiene.

| # | Item | Priority | Type | Target | Effort | Status |
|---|------|----------|------|--------|--------|--------|
| 43 | PredicateBuilder&lt;T&gt; fluent API | P1 | Feature | post-1.0 | Medium | Open |
| 12 | Hedging: HedgingDelayGenerator | P1 | Feature | post-1.0 | Low | Open |
| 41 | Pipeline dynamic reload via IOptionsMonitor | P1 | Feature | post-1.0 | High | Open |
| 42 | Keyed services support | P1 | Feature | post-1.0 | Medium | Open |
| 39 | Resilion.Http — HttpClient integration | P1 | Feature | post-1.0 | High | Open |
| 27 | Retry typed/untyped strategy duplication (4 loops) | P1 | Improvement | post-1.0 | Medium | Open |
| 30 | FallbackAction/ResilienceEventHandler Task.Run missing token | P1 | Fix | post-1.0 | Low | Open |
| 9 | Sync retry delay bypasses TimeProvider (untestable) | P1 | Fix | post-1.0 | Low | Open |
| 54 | Evaluate multi-targeting net9.0 for System.Threading.Lock | P1 | Improvement | post-1.0 | Medium | Open |
| 45 | Telemetry enrichment (MeteringEnricher, listeners) | P2 | Feature | unscheduled | High | Open |
| 44 | IConfiguration binding for strategy options | P2 | Feature | unscheduled | Medium | Open |
| 40 | Resilion.Chaos — chaos engineering | P2 | Feature | unscheduled | High | Open |
| 11 | Hedging: O(N²) WhenAny continuation registration (needs #54) | P2 | Improvement | unscheduled | Medium | Open |
| 10 | Resilion.Testing package | P2 | Feature | unscheduled | Low | Open |
| 4 | Per-call delegate allocation in pipeline chain | P2 | Improvement | unscheduled | High | Open |
| 5 | CancellationTokenSource pooling for Timeout | P2 | Improvement | unscheduled | Medium | Open |
| 48 | Benchmark CI on PRs | P3 | Infrastructure | unscheduled | Low | Open |
| 47 | CI code coverage collection | P3 | Infrastructure | unscheduled | Medium | **Blocked** |

---

## P0 — Must land before 1.0.0

_Empty._ Everything that was blocking 1.0.0 has shipped — see `## Retired items` and
[CHANGELOG.md](../CHANGELOG.md). A new entry belongs here only if it makes something the library
already claims untrue, or produces a silently wrong answer.

## P1 — Next (post-1.0)

### 43. `PredicateBuilder<T>` Fluent API

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -rn 'PredicateBuilder' src/ --include='*.cs'` → 0 hits.

**Why**

Polly provides `new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().HandleResult(r => r.StatusCode == 500)`
with implicit conversion to a predicate delegate. Resilion requires writing the full
`Func<Outcome<T>, bool>`. For simple cases that is fine and arguably clearer; for complex predicates
it gets verbose. Named as a gap in [comparison-with-polly.md](comparison-with-polly.md).

**Design**

`PredicateBuilder<TResult>` with `Handle<TException>()`, `Handle<TException>(Func<TException, bool>)`,
`HandleInner<TException>()` (+ predicate overload), `HandleResult(Func<TResult, bool>)`,
`HandleResult(TResult)`, `Build()`, and an implicit conversion to `Func<Outcome<TResult>, bool>`. Plus
a non-generic `PredicateBuilder` producing `Func<Exception, bool>` for the exception-only options
(`RetryStrategyOptions.ShouldHandle`, `CircuitBreakerStrategyOptions.ShouldHandle`) — without it,
those are the one place the sugar doesn't reach.

**Two design rules that keep it from becoming a second way to do the same thing**

1. **`Build()` throws `InvalidOperationException` on an empty builder.** The default
   "handle everything except cancellation" predicate is reached exactly one way — `ShouldHandle == null`
   — and every options record funnels through that. A `PredicateBuilder` always produces a non-null
   delegate, so it can never *be* the default. The only way it could become a second path is an empty
   builder silently meaning "handle everything". Throwing closes that.
2. **It does not inherit the cancellation carve-out.** `OutcomePredicates.DefaultShouldHandle<T>`
   excludes `OperationCanceledException`; `new PredicateBuilder<T>().Handle<Exception>()` matches it.
   That is correct for an explicit builder, and it must be **called out in the docs** rather than
   discovered.

Clauses combine with OR. `Build()` snapshots them into an array so the returned delegate is unaffected
by later mutation. Deliberately **not** single-use, unlike `PipelineBuilder` — this is a
value-producing helper and reuse is natural. Document the reasoning for the inconsistency.

**Effort:** Medium. Self-contained; zero risk to the hot path.

---

### 12. Hedging: `HedgingDelayGenerator`

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'HedgingDelay' src/Resilion/Hedging/HedgingStrategyOptions.cs`
→ a static `TimeSpan` property only.

**Why**

The current `HedgingDelay` is a static `TimeSpan` — the same delay for every hedged attempt. Some
scenarios benefit from dynamic delays (shorter delays for later attempts, or delays computed from
recent latency percentiles).

**Design**

Add `Func<HedgingDelayGeneratorArgs, TimeSpan>? HedgingDelayGenerator` to
`HedgingStrategyOptions<T>`. When set, `HedgingDelay` is ignored.

```csharp
public readonly record struct HedgingDelayGeneratorArgs(int AttemptNumber, ResilienceContext Context);
```

`AttemptNumber` is the 0-based index of the attempt **about to be launched** (so `1` for the first
hedge), consistent with `HedgingActionContext.AttemptNumber` and `OnHedgingEvent<T>.AttemptNumber`.

Consult it as the first statement of `ExecuteAsync`'s attempt loop and use the result at all three
sites that currently read `_options.HedgingDelay` — the `InfiniteTimeSpan` sequential-mode check, the
`> TimeSpan.Zero` latency-mode check, and the `Task.Delay`. **This makes mode selection per-attempt**:
a generator can return `InfiniteTimeSpan` for one attempt and `Zero` for the next. That is the
feature; document it. Clamp a negative non-`InfiniteTimeSpan` result to `TimeSpan.Zero` rather than
throwing mid-execution.

The sync guard must also reject a generator being set (it cannot be evaluated on the synchronous
path), and `Validate()` must skip the static `HedgingDelay` range check when a generator is present.

**Effort:** Low.

---

### 41. Pipeline Dynamic Reload via `IOptionsMonitor`

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'Invalidate\|IChangeToken\|IOptionsMonitor' src/Resilion.Extensions/*.cs`
→ 0 hits.

**Why**

Polly supports `context.EnableReloads<TOptions>()`, which auto-recreates pipelines when
`IOptionsMonitor<T>` detects a configuration change. Without this, changing retry counts or timeout
durations requires an app restart.

**Design**

The registry's existing shape already solves the single-use-builder problem: `GetPipeline` stores the
`Action<PipelineBuilder>` in `_factories` and constructs a **fresh** `PipelineBuilder` inside the
`Lazy<Pipeline>` factory. So **reload = drop the `Lazy`, keep the factory.** Invalidation is
`_pipelines.TryRemove(key, out _)`; the next `GetPipeline` rebuilds. `_factories` is never touched, so
the "a failed lookup doesn't poison the cache" invariant survives unchanged.

New public surface on `ResiliencePipelineRegistry<TKey>`: `InvalidatePipeline(TKey)`,
`InvalidatePipeline<TResult>(TKey)`, `TryGetPipeline<TResult>(TKey, out Pipeline<TResult>?)` (fills an
existing asymmetry), and `Action<PipelineReplacedArgs<TKey>>? OnPipelineReplaced`. New
`AddResiliencePipeline<TOptions>` / `<TResult, TOptions>` overloads taking a 2-arg
`Action<PipelineBuilder, TOptions>` delegate — unambiguous against the existing 1-arg overloads.

**`IChangeToken` support is not needed in the registry.** `IOptionsMonitor<T>.OnChange(listener)`
covers the DI path, and a caller outside DI writes
`ChangeToken.OnChange(producer, () => registry.InvalidatePipeline(key))` with zero new API.

**`IPipelineProvider<TKey>` needs no change** — `GetPipeline` re-reads the dictionary on every call, so
a consumer holding the interface gets the new pipeline automatically. That is why reload works without
an interface break, and it makes the documented contract: **with reload enabled, resolve per call;
never cache the `Pipeline` in a field.** (Keyed injection, #42, violates this by construction.)

**In-flight executions** complete on the old pipeline — it is immutable and the executing frame holds
a strong reference through its own stack. No tearing.

**Replaced pipelines are not disposed by the registry.** It cannot know when the last in-flight
execution finishes without ref-counting every execute path. Verified: no strategy in the repo
overrides `Dispose`, and `DelegatingComponent.Dispose` deliberately does not dispose composed inner
components ([tradeoffs.md](tradeoffs.md)) — so auto-disposal would be both useless today and unsafe
once a strategy owns a resource. Hand the old pipeline to `OnPipelineReplaced` and document that the
caller decides.

**Eventual consistency:** concurrent invalidate + get can return a pipeline built from options that
were current a few microseconds ago. Document it; do not add locking.

Internally this needs an `IRegistryConfigurator` that runs **before** `IPipelineConfigurator` in
`BuildRegistry`, and `IPipelineConfigurator.Configure` gaining an `IServiceProvider` parameter (it is
internal, so that's free). This is what finally uses the `Microsoft.Extensions.Options` reference that
has sat unused in `Resilion.Extensions.csproj` since the package was created.

**Effort:** High.

---

### 42. Keyed Services Support

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'Keyed' src/Resilion.Extensions/*.cs` → 0 hits.

**Why**

.NET 8 introduced keyed DI services. Polly v8.3+ supports this for direct injection of named pipelines
without going through the registry.

**Design**

Add `TryAddKeyedSingleton` registrations inside the existing `AddResiliencePipeline` overloads, with
the factory deferring to the registry. The `(TKey, Type)` composite typed store maps directly onto
keyed DI: keyed service type `Pipeline<TResult>` + service key `name` ≡ `(name, typeof(TResult))`.
`TryAdd*` so a duplicate call doesn't add a second descriptor — the registry already throws on
duplicate keys at build time, which is where that error belongs.

**Zero new public API.** The consumer-facing surface is `[FromKeyedServices("name")] Pipeline`, which
is BCL API.

**Two hazards:**

1. **Double dispose — hard prerequisite.** A keyed *singleton* resolved from a factory is tracked and
   disposed by the root container, **and** `ResiliencePipelineRegistry.Dispose()` disposes every
   created pipeline. The same instance is disposed twice — harmless today, a real bug the day a
   strategy owns a resource. **`Pipeline.Dispose`/`DisposeAsync` and the typed equivalents need a
   `_disposed` guard** so the second call is a no-op. Keyed *transient* is not an escape: DI disposes
   transient `IDisposable`s resolved from a scope, which would destroy the registry's cached pipeline
   mid-flight. Singleton is the only correct lifetime.
2. **Keyed injection defeats #41.** A keyed singleton caches the pipeline for the container's
   lifetime, and an injected field caches it again. Neither sees an invalidation. Inherent, not
   fixable — document it as a hard incompatibility in both the reload docs and the DI docs.

**Effort:** Medium.

---

### 39. Resilion.Http — HttpClient Integration

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-09-30 — no `src/Resilion.Http` directory; `grep -rn 'IHttpClientBuilder' src/` → 0 hits.

**Why**

The most common use case for a resilience library is wrapping `HttpClient` calls. Polly's
`Microsoft.Extensions.Http.Resilience` provides `AddStandardResilienceHandler()`, which adds a
pre-configured pipeline to an `HttpClient` via `IHttpClientFactory`. Without an equivalent, Resilion
misses the #1 onboarding path — and [comparison-with-polly.md](comparison-with-polly.md) says so
outright.

**Design**

New package `Resilion.Http` with `AddStandardResilienceHandler()` on `IHttpClientBuilder`
(RateLimiter → TotalRequestTimeout → Retry → CircuitBreaker → AttemptTimeout, retrying on 5xx, 429,
408, `HttpRequestException` and `TimeoutRejectedException`), `AddStandardHedgingHandler()`, and
`AddResilienceHandler(key, builder => …)`. Implemented as a `DelegatingHandler` wrapping a
`Pipeline<HttpResponseMessage>`. References `Resilion.Extensions` and `Microsoft.Extensions.Http`.

**It needs no `InternalsVisibleTo`** — every member required is public, because the typed `Add*`
overloads call the internal tagged `AddStrategy` on our behalf (so ordering validation still works),
and `ResilienceContextPool.Shared.Rent(ct)` plus `Pipeline<T>.ExecuteOutcomeAsync` cover context
lifecycle and non-throwing execution.

**Four design points that are easy to get wrong:**

1. **Never build the pipeline inside the `AddHttpMessageHandler` factory.** That factory re-runs on
   every `IHttpClientFactory` handler rotation (default 2 minutes), so a pipeline built there would
   reset circuit-breaker and rate-limiter state every 2 minutes — a silent correctness bug. Register
   through `AddResiliencePipeline<HttpResponseMessage>(key, …)` and resolve from the registry.
2. **Request reuse vs cloning.** Retrying the same `HttpRequestMessage` is what Polly does and is
   legal (`HttpClient`'s "already sent" guard sits above the handler), but only for replayable
   content. Offer an explicit `HttpRequestReplay` mode that buffers the body once and clones per
   attempt, forced on for hedging (concurrent attempts cannot share one message).
3. **Discarded responses must be disposed.** `RetryStrategy<T>` overwrites its outcome each loop and
   hedging abandons losers, neither disposing. Leaked `HttpResponseMessage` instances hold sockets.
   `OnRetry` cannot be used for this, because `ResilienceEventHandler<TArgs>.InvokeAsync` is internal
   and a user-supplied handler can only be overwritten, not chained — so the handler needs its own
   attempt tracker, including a "closed" state for hedging stragglers that complete after the handler
   has already returned.
4. **Override `Send` to throw.** `DelegatingHandler.Send` forwards to the inner handler, which would
   silently bypass the entire resilience pipeline. `Pipeline<T>` has no synchronous `ExecuteOutcome`,
   so a correct sync implementation isn't currently possible.

**Known gap:** `Retry-After` honouring is impossible today, because `RetryDelay.Custom` receives only
the attempt number. See the outcome-aware-retry-delay item below; it is the largest remaining
functional gap versus Polly's HTTP package, and it matters most for the 429 responses this pipeline
retries by default with blind exponential backoff.

**Performance note:** HttpClient pipelines are a hot path in high-throughput services. The per-call
delegate allocation (#4) is acceptable for general use but may start showing up in profiles once this
package ships. Benchmark the standard handler under load — against
`Microsoft.Extensions.Http.Resilience`, not hand-wrapped `Polly.Core`, or you are measuring our
handler against our own Polly handler — and revisit #4 if it becomes a measurable cost.

**Release note:** `release-nuget.yml` packs three projects by explicit path and hard-asserts a package
count of 3. **That must become data-driven before this package lands**, or every release fails.

**Effort:** High.

---

### 27. Retry Typed/Non-Generic Strategy Duplication

**Status:** Open
**Type:** Improvement
**Target:** post-1.0
**Blocks:** nothing, but see #56 — the span-scaffolding extraction there is the part #56 needed.
**Verified:** 2026-09-30 — `grep -c 'for (var attempt = 0' src/Resilion/Retry/RetryStrategy.cs` → 4;
`wc -l src/Resilion/Retry/RetryStrategy.cs` → 302.

**Why**

`RetryStrategy` / `RetryStrategy<T>` hold **four independent retry loops** — `:19-103` (~85 lines),
`:105-181` (~77), `:198-250` (~53), `:252-301` (~50). Each carries its own `for`, predicate check,
`MaxDelay` clamp, telemetry increment, `OnRetry` invocation and delay wait. The equivalent circuit
breaker duplication was already extracted into a shared `CircuitBreakerStateMachine` — and that
extraction is exactly what let the CB race-condition fix apply to both variants automatically instead
of being kept in sync by hand.

**Risk:** a bug fix applied to one copy but not the other causes silent behavioural divergence between
the typed and non-generic variants. This already happened once: the untyped pair is ~30 lines longer
than the typed pair *because* it carries span code the typed pair lacks (#56).

**Fix**

Extract a shared retry loop. It must be parameterized over two things that genuinely differ: the
predicate (untyped: an `outcome.IsSuccess` early return plus `ShouldHandleException(outcome.Exception!)`;
typed: `ShouldHandleOutcome(outcome)`) and the `OnRetry` argument type (`RetryAttemptEvent` vs
`RetryAttemptEvent<TResult>`).

**Do it only with the benchmark numbers in hand.** Delegate-based parameterization adds per-execution
closure allocations to the library's most-advertised hot path (114 ns / 192 B for a single retry, 61 ns
/ 192 B sync). An allocation-free version needs a generic-struct-constrained-interface shim so the JIT
specializes. Either way, commit the re-run benchmark results with the change.

**Safe to attempt once #55 and #56 have landed** — their parity tests
(`HedgingAttempts_SyncAndAsyncAgreeOnCount`, `TypedAndUntypedPipelines_EmitTheSameSpanNames`) pin the
observable behaviour of all four paths before the loops merge. **Public API: none**, if done as an
internal shared loop or internal shim. If the design starts wanting a public extension point, stop
and get that approved separately.

**Effort:** Medium.

---

### 30. FallbackAction/ResilienceEventHandler `Task.Run` Missing `CancellationToken`

**Status:** Open
**Type:** Fix
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'Task.Run' src/Resilion/Fallback/FallbackAction.cs src/Resilion/ResilienceEventHandler.cs`
→ `FallbackAction.cs:64`, `ResilienceEventHandler.cs:93`, neither passing a token.

**Why**

Both `FallbackAction<T>.Execute` and `ResilienceEventHandler<T>.Invoke` call
`Task.Run(() => …).GetAwaiter().GetResult()` for sync-over-async without passing a
`CancellationToken`.

**Be accurate about the benefit.** `Task.Run(f, ct)` only declines to *schedule* work when `ct` is
already cancelled. It cannot interrupt a running handler, and the surrounding
`.GetAwaiter().GetResult()` blocks the calling thread regardless. So this is correct hygiene, not a
cancellation fix — which is why it is not a blocker.

**Fix**

Cheap and API-free. `ResilienceEventHandler<TArgs>.Invoke` is internal, so it can take a
`CancellationToken cancellationToken = default` parameter with zero public-API impact, threaded from
`context.CancellationToken` at each internal call site. `FallbackContext<TResult>` already carries
`Context.CancellationToken`, so `FallbackAction.Execute` needs no signature change at all.

**Tests must force a `SynchronizationContext`** to reach the `Task.Run` branch — the handler
short-circuits the thread hop entirely when `SynchronizationContext.Current is null`, which is the
common case and the default under xunit.

**Effort:** Low.

---

### 9. Sync Retry Delay Bypasses `TimeProvider` (Untestable)

**Status:** Open
**Type:** Fix
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'WaitHandle' src/Resilion/Retry/RetryStrategy.cs`
→ `:170` and `:295`, both `context.CancellationToken.WaitHandle.WaitOne(delay)`.

**Why**

This entry previously claimed that `CancellationToken.WaitHandle` "lazily allocates a
`ManualResetEvent` that is never explicitly disposed … on every retry delay". **That is wrong.** The
handle is allocated **once per `CancellationTokenSource`**, is disposed with the CTS, and for a
default (`CanBeCanceled == false`) token the runtime uses a static never-signalled event with no
allocation at all. Across a retry loop the cost amortises to near zero, and the obvious alternatives
are worse: `Thread.Sleep` loses cancellation responsiveness, spin-polling burns CPU.

**The real defect** is that `RetryStrategy.cs:170` and `:295` wait via `WaitHandle.WaitOne(delay)`,
**bypassing `_timeProvider` entirely**. `builder.TimeProvider` is this repo's single time-injection
seam — it is how every other timing test uses `FakeTimeProvider`. So **synchronous retry delays cannot
be driven by a fake clock, and no sync retry-timing test can be deterministic.**

**Fix**

Needs a sync-waitable abstraction over `TimeProvider` — something that can block the calling thread
for a provider-controlled duration while remaining cancellation-responsive. That is a design change,
not a patch, which is why this is not a 1.0 item. Do it when someone actually needs deterministic
sync-retry timing coverage.

**Effort:** Low to write, medium to design well.

---

### 54. Evaluate Multi-Targeting net9.0 for `System.Threading.Lock`

**Status:** Open
**Type:** Improvement
**Target:** post-1.0
**Verified:** 2026-09-30 — `grep -n 'TargetFramework' src/Directory.Build.props` → `net8.0` (singular);
`grep -rn 'System.Threading.Lock\|NET9_0_OR_GREATER' src/ --include='*.cs'` → 0 hits.

**Why**

.NET 9 introduced `System.Threading.Lock`, a purpose-built synchronization primitive faster than
`lock(object)` under contention. `CircuitBreakerStateMachine.cs:23` and `SlidingWindow.cs:20` both hold
a lock on every call while in the Closed state — the most common state in healthy systems.

**Plan the experiment, not the change.**

The existing `CircuitBreakerLoadBenchmarks` **cannot answer this question** — it is single-threaded,
and `System.Threading.Lock`'s advantage is a contention-path property. Two new benchmarks are required:

1. `LockPrimitiveBenchmarks` — an isolated micro-benchmark that does not reference the library. Two
   standalone copies of `SlidingWindow`'s hot path (record a sample + compute the ratio, inside the
   lock), one per lock type behind `#if NET9_0_OR_GREATER`, with `[Params(1, 4, 16, 64)] int Threads`
   driving concurrent callers against one instance. The benchmarks project already multi-targets
   `net8.0;net10.0`, so this compiles as-is.
2. `CircuitBreakerLoadBenchmarks.Closed_MixedTraffic_Concurrent` with
   `[Params(1, 4, 16, 64)] int Concurrency`. **Needed regardless of this item** — the suite currently
   has no contention coverage for the library's only lock-per-call path.

**Decision rule — agree it before benchmarking, so the outcome isn't negotiated after the numbers
land.** Proceed only if all three hold:

- **(a)** At `Threads >= 16`, `System.Threading.Lock` is **>= 5% faster in mean wall-clock** in the
  micro-benchmark, with non-overlapping confidence intervals.
- **(b)** The same **>= 5%** shows up end-to-end at `Concurrency >= 16`. A micro-benchmark win that
  doesn't survive the full pipeline is not a user-visible win.
- **(c)** At 1 and 4 threads, the new lock is **not more than 2% slower**. The uncontended case is the
  common one; a regression there is a **veto**.

**If it passes**, the code change is trivial but the cost is not: `<TargetFrameworks>net8.0;net9.0</TargetFrameworks>`
affects every src project; `test.yml`'s matrix varies the *SDK*, not the TFM, so it needs a TFM
dimension or the net9.0 assemblies are built but never tested; `ci.yml`'s `aot-verify` hardcodes a
`net8.0` publish path; and `release-nuget.yml` must pack both TFMs. **Keep the change purely
internal** so `PublicAPI.*.txt` stays TFM-invariant.

**Expected outcome: it fails its own gate.** The documented win is concentrated in the uncontended
fast path and in avoiding `Monitor`'s thread-static lookups; clearing 5% at the full-pipeline level,
where an execution is already hundreds of nanoseconds, is a high bar. **If it fails, commit the
numbers to `benchmarks/results/` and rewrite this entry as "Evaluated and rejected", with a link** —
so it stops reading as pending work.

**Effort:** Medium, almost entirely benchmarking time.

---

## P2 — Backlog

### 45. Telemetry Enrichment

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Depends on:** #56 (span coverage must be complete first)
**Verified:** 2026-09-30 — `grep -n 'static readonly' src/Resilion/Telemetry/ResilionTelemetry.cs`
→ `Meter`, `ActivitySource` and all six counters are static; no per-pipeline hook exists.

**Why**

Polly supports `MeteringEnricher` (custom tags on all telemetry events), `TelemetryListeners` (raw
event listeners), and `SeverityProvider` (adjust/suppress severity). Resilion's telemetry is static
counters only.

**Design**

Enrichment needs per-pipeline state, but the `Meter` and `ActivitySource` are static. The channel
already exists: **`ResilienceContext`**, which every execute entry point already stamps with
`PipelineName`. Add `internal ResilionTelemetryOptions? Telemetry` to the context (cleared in
`Reset()`), a `public ResilionTelemetryOptions? TelemetryOptions` on `PipelineBuilderBase`, and thread
it through `Build()` into the `Pipeline` constructor — **including the two
`Name is not null ? … : Pipeline.Empty` early-return branches**, which otherwise silently drop the
options for empty pipelines.

Then replace every `counter.Add(…)` with an `internal static TelemetryEmitter.Emit(instrument, in resilienceEvent, context)`
that resolves severity, returns early on `None` (suppression), emits, and invokes listeners.

**Preserving the zero-cost path is mandatory.** When `context.Telemetry` is null or carries no
enrichers, `Emit` must use the existing two-tag `counter.Add(1, tag1, tag2)` overload —
byte-for-byte the current behaviour. Only with enrichers present does it build a `TagList` (a struct
with inline storage for 8 tags, so even the enriched path stays allocation-free for typical tag
counts). [telemetry.md](telemetry.md)'s "zero-cost when unused" claim must remain true, and a
benchmark should prove it.

**The plumbing goes in core, not `Resilion.Extensions`** (an earlier draft of this entry said
otherwise). The emit sites are in core and core cannot depend on Extensions. Extensions adds only the
`ILogger` bridge — which is what finally justifies its unused `Logging.Abstractions` reference.

**Be honest about severity:** core has no logger, so severity is only meaningful for suppression via
`None` and for user listeners. Say that rather than implying log-level integration.

**Hard prerequisite:** ship #56 first. Enrichment layered on top of partial span coverage gives users
enrichment that silently doesn't apply on typed pipelines — worse than no enrichment.

**Effort:** High. This is the most invasive item in the backlog: every emit site in every strategy.

---

### 44. `IConfiguration` Binding for Strategy Options

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Depends on:** #41 (without reload, config changes still require a restart)
**Verified:** 2026-09-30 — `grep -n 'private RetryDelay\|public static RetryDelay' src/Resilion/Retry/RetryDelay.cs`
→ private constructor, factory methods only.

**Why**

Bind strategy options from `appsettings.json` so timeouts, retry counts and thresholds can change
without recompiling.

**Design — do not make `RetryDelay` bindable.**

`RetryDelay` is a closed union with a private constructor, deliberately not independent properties
([comparison-with-polly.md](comparison-with-polly.md)). Making it bindable reintroduces exactly the
Polly ambiguity this library rejected.

Instead add a one-way **binding DTO layer** in a `Resilion.Extensions.Configuration` namespace:
mutable POCOs with parameterless constructors (what `ConfigurationBinder` and its source generator
require) plus a `ToOptions()` projection into the real `sealed record` options. The union stays
closed; the DTO is a validated funnel into it.

Prevent ambiguity with an **explicit discriminator plus a throwing validator**, not by hoping:
`RetryDelayConfiguration.Kind` selects the variant, and `ToRetryDelay()` **throws** when a property is
set that the chosen `Kind` ignores (e.g. `Kind = Constant` with `MaxDelay` set, since
`RetryDelay.Constant` has no cap). Silently ignoring it is what Polly does and what this library
exists not to do. **`Custom` is deliberately absent from the kind enum** — a delegate cannot come from
JSON, and pretending otherwise is the ambiguity in a new costume.

Delegates are not on the DTOs at all. Users layer them with a record `with` expression:

```csharp
services.Configure<RetryStrategyConfiguration>(config.GetSection("Resilience:Api:Retry"));
services.AddResiliencePipeline<RetryStrategyConfiguration>("api", (b, c) =>
    b.AddRetry(c.ToOptions() with { ShouldHandle = ex => ex is HttpRequestException }));
```

Every DTO default must mirror the corresponding options record exactly, so "bind nothing" ≡ "pass no
options". Drift there is a silent bug; pin it with tests.

No new package dependency — the DTOs are plain POCOs, so the consumer's own
`Microsoft.Extensions.Options.ConfigurationExtensions` does the binding.

**Explicitly out of scope: whole pipelines from JSON** — a strategy array with type discriminators and
ordering from config. It would put `OrderingValidator`'s guarantees at the mercy of a JSON file while
still requiring code for every predicate: half-configured pipelines with worse diagnostics. Say so in
the docs.

**Effort:** Medium.

---

### 40. Resilion.Chaos — Chaos Engineering

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Verified:** 2026-09-30 — no `src/Resilion.Chaos` directory; `grep -n 'Chaos' src/Resilion/Internal/StrategyType.cs`
→ 0 hits.

**Why**

Polly v8.3+ includes Simmy for chaos engineering — fault, outcome, latency and behavior injection.
Table stakes for production resilience testing, and the biggest real feature gap after
`Resilion.Http`.

**Design**

New package `Resilion.Chaos` with four strategies: `AddChaosFault`, `AddChaosOutcome`,
`AddChaosLatency`, `AddChaosBehavior`. Shared options on a public abstract record base:
`InjectionRate`, `InjectionRateGenerator`, `Enabled`, `EnabledGenerator` — **generators take
precedence over static values**, resolved in one shared internal helper so the four strategies cannot
diverge. Zero `PackageReference`s (one `ProjectReference` to `Resilion`), preserving the
zero-dependency story.

**`Randomizer` (`Func<double>?`) is not optional polish.** Without it, chaos strategies are as
untestable as a strategy that ignores `builder.TimeProvider` — it is the injection-decision analogue
of the time seam. Tests set `() => 0.0` (always inject) or `() => 1.0` (never). Document it as the
supported test seam.

**Needs `InternalsVisibleTo`** for three things: `Internal.StrategyType.Chaos` (for the tagging
`AddStrategy` overloads), a new internal chaos counter, and `ResilienceEventHandler<T>.Invoke` (to run
the behavior payload). Same pattern as `Resilion.RateLimiting`.

**Chaos must be innermost, and the validator should say so.** Chaos outside Retry means retry never
sees the injected fault, so the chaos run *silently tests nothing*. Add `Chaos` to `StrategyType`
(**appended**, to keep existing ordinals stable) and one **warning** in `OrderingValidator` when a
chaos strategy is not in the innermost block. Warning rather than error, because stacking several
chaos strategies innermost is legitimate and a deliberate mid-pipeline injection is conceivable.

Other mechanics worth recording up front: `AddChaosOutcome` is **typed-only** on purpose, because a
typed strategy added to an untyped pipeline is silently skipped by `TypedStrategyComponent<T>`;
`ChaosLatencyStrategy` must take `builder.TimeProvider`; all four must override the sync `Execute`
with real implementations rather than inheriting the base that throws under a
`SynchronizationContext`; and `FaultGenerator` should be documented as preferable to a static `Fault`,
because `Outcome<T>.FromException` captures with `ExceptionDispatchInfo` and rethrowing one instance
repeatedly accumulates stack traces.

**Production safety:** with defaults of `Enabled = true` and a non-zero `InjectionRate`, lead the docs
with `EnabledGenerator = _ => !env.IsProduction()`.

**Release note:** as with #39, `release-nuget.yml`'s hardcoded package count must be data-driven
first.

**Effort:** High.

---

### 11. Hedging: O(N²) `Task.WhenAny` Continuation Registration

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Depends on:** #54 (the clean fix is net9.0+ only)
**Verified:** 2026-09-30 — `grep -n 'Task.WhenAny\|Task.WhenEach' src/Resilion/Hedging/HedgingStrategy.cs`
→ `WhenAny` at `:69`, `:70`, `:143`, `:333`; no `WhenEach`.

**Why**

This entry was previously titled "memory leak". **That overstates it.** `WaitForBestOutcome`
(`:322-347`) calls `Task.WhenAny(remaining)` once per iteration, so it registers O(N²) continuations
where N = `MaxHedgedAttempts`, and continuations from earlier iterations attached to still-pending
tasks are never deregistered. But N is a small user-chosen constant (typically 2–3), the cleanup in
`finally` awaits all tasks, and it only "leaks" if an attempt never completes — in which case the
attempt's own `Task` is already leaked and the continuation is the least of the problem.

**Fix**

`Task.WhenEach` is the clean answer, and it is **net9.0+ only** — hence the dependency on #54's
multi-targeting decision.

**Do not hand-roll a net8-compatible equivalent.** A `ContinueWith` + `SemaphoreSlim` +
`ConcurrentQueue` implementation trades a bounded O(N²) continuation registration for *more*
allocations at the N this code actually runs at. Net negative. That analysis is recorded here so the
next reader doesn't redo it.

**Do not "simplify" the other `WhenAny` sites.** `:69-70` is the hedging-delay race and is correct as
written — the comment at `:74-77` explains why it deliberately awaits the stored task rather than
calling `WhenAny` twice. `:143` is the cleanup timeout.

**Effort:** Medium, and gated.

---

### 10. Resilion.Testing Package

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Verified:** 2026-09-30 — `grep -rn 'CircuitState' src/Resilion/CircuitBreaker/CircuitBreakerStrategyOptions.cs`
→ no state-provider member; `grep -n 'internal string? Name' src/Resilion/Pipeline.cs` → `:47`.

**Why (and why this is deferred rather than planned)**

[comparison-with-polly.md](comparison-with-polly.md) lists a testing package as a gap. Walking through
what it would actually provide:

| Claimed capability | Reality |
|---|---|
| Deterministic time | `FakeTimeProvider` via `builder.TimeProvider` already covers it; see [testing.md](testing.md). **No gap.** |
| Custom context construction | `ResilienceContext`'s constructor is internal, **but `ResilienceContextPool.Shared.Rent(ct)` is public** and `OperationKey`/`Properties`/`ContinueOnCapturedContext` are public setters. **No gap.** |
| Recording executions | Typed pipelines already have the inline `AddStrategy(string, Func<…>)` overload. Untyped pipelines don't — and can't, because the untyped path needs a *method-level* generic delegate, which C# cannot express as a field. Deriving from `Strategy` works (`protected internal`). **Marginal.** |
| Telemetry capture | `MeterListener`/`ActivityListener` work today; this repo's own `TelemetryTests` does exactly that. A helper would cut boilerplate. **Marginal.** |
| **Circuit breaker state assertions** | **Real, blocking gap.** There is no public way to read a circuit's current `CircuitState` — `CircuitBreakerManualControl` only isolates and resets. **This cannot be built in a satellite package at all.** |
| Pipeline name assertions | `Pipeline.Name` / `Pipeline<T>.Name` are internal — the only thing that would force `InternalsVisibleTo`. |

So the concrete content is **one missing core API plus one visibility change**, not a package. A
package here would be a thin wrapper whose main value is working around core omissions.

**Ship these instead:**

- `public sealed class CircuitBreakerStateProvider` with a `CircuitState State` property, plus
  `StateProvider` on both circuit breaker options records. Mirror
  `CircuitBreakerManualControl`'s existing bind-once pattern (`internal void Initialize(…)`, throws if
  already bound), wired in `CircuitBreakerStateMachine`; `State` throws when unbound, matching
  `IsolateAsync`.
- `Pipeline.Name` / `Pipeline<TResult>.Name` internal → **public**. Useful for logging on its own, and
  it removes the only `InternalsVisibleTo` a future testing package would need.

**Evidence that would justify the package later:** (a) three or more distinct issues or discussions
asking for test helpers; (b) a helper class from this repo's own test suite being copy-pasted into a
user issue; (c) `Resilion.Http` shipping — that creates genuine demand for `TestHttpMessageHandler`-shaped
doubles, which belong in a `Resilion.Http.Testing`, not a general `Resilion.Testing`.

**Effort:** Low for the two core additions; the package itself is deferred.

---

### 4. Per-Call Delegate Allocation in Pipeline Chain

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Verified:** 2026-09-30 — `grep -n 'ctx => _next.ExecuteAsync' src/Resilion/Internal/PipelineComponent.cs`
→ `:70`.

**Why**

In `StrategyComponent.ExecuteAsync`, the lambda `ctx => _next.ExecuteAsync(callback, ctx)` creates a
closure capturing `_next` and `callback` on every call. In a pipeline with N strategies that is N small
closure allocations per execution.

**Current state**

Inherent to the middleware/chain-of-responsibility pattern; Polly v8 has the same cost. Confirmed
empirically in [benchmarks/results](../benchmarks/results/README.md): Resilion's happy-path pipelines
allocate a few hundred bytes where Polly's are allocation-free, but Resilion is still faster
wall-clock across every shape benchmarked.

This item is tracked **here only**. [tradeoffs.md](tradeoffs.md) previously also carried it as an
accepted imperfection, which meant the repo simultaneously claimed the fix was worse than the problem
and that the fix was planned. That duplicate now points here.

**The measurement that would settle it is missing.** Every current benchmark uses a
synchronous-completing no-op callback, which maximizes the *relative* weight of the allocations and
says nothing about whether they matter. Add an `AllocationImpactBenchmarks` using the real-world
pipeline shape with a callback simulating **1 ms of I/O**, `[MemoryDiagnoser]`, and Gen0 collections
reported per 10 000 operations.

**Decision rule:** pursue the pre-composed-delegate-chain redesign only if, at 1 ms per-call latency,
**either** the allocation difference moves throughput by **>= 3%**, **or** Gen0 collections exceed
**1 per 10 000 operations**.

**Expected outcome: both false.** At 1 ms/op, ~976 B/op is under 1 MB/s of Gen0 traffic — inside the
noise for any .NET server. **If so, close this as "measured, won't fix"** rather than leaving it as
"revisit", and record the numbers. What would reopen it: a Gen0 allocation profile from a real
workload — most plausibly one surfaced by `Resilion.Http` (#39) — showing these closures in the top
five allocators.

Note also that the proposed fix "requires complex generic type threading and may not be feasible
without sacrificing API simplicity". In a library whose entire pitch is simplicity, that is close to
disqualifying on its own.

**Effort:** High.

---

### 5. CancellationTokenSource Pooling for Timeout

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Verified:** 2026-09-30 — `grep -n 'CreateLinkedTokenSource\|CreateTimer' src/Resilion/Timeout/TimeoutStrategy.cs`
→ CTS at `:41`, timer at `:48-63`, one of each per execution.

**Why**

The Timeout strategy allocates one `CancellationTokenSource` per execution.
`CancellationTokenSource.TryReset()` (since .NET 6) enables pooling — reset and reuse instead of
allocating.

**The missing measurement**

There is no isolated timeout benchmark at all. Add a `TimeoutAllocationBenchmarks` with (a) `AddTimeout`
alone on the happy path where the timeout never fires, and (b) the same where it **does** fire — the
case where `TryReset()` returns `false` and pooling yields nothing.

**Decision rule:** implement pooling only if the CTS + `ITimer` pair is **> 25%** of total bytes/op for
a timeout-only pipeline **and** pooling removes **>= 20%** of bytes/op with no wall-clock regression.

**The ceiling is lower than this issue implies.** `_timeProvider.CreateTimer(…)` also allocates an
`ITimer` per execution, so pooling only the CTS caps the upside at roughly half. The larger lever —
`CancellationTokenSource.CancelAfter`, which rides the shared timer queue instead of allocating —
**breaks `FakeTimeProvider` testability**, and `builder.TimeProvider` is the repo's single
time-injection seam. That is a hard no. So if this is done at all it must pool the **pair** behind one
policy, which is materially more complex than a "Medium" estimate and carries the `TryReset()`
timer-state edge cases noted below.

Note that #46 *adds* one small allocation to this path, so re-baseline before measuring.

**Complexity:** Medium-to-high. `TryReset()` has edge cases around timer state and concurrent
cancellation.

**Effort:** Medium.

---

## P3 — Blocked / needs external change

### 48. Benchmark CI on PRs

**Status:** Open
**Type:** Infrastructure
**Target:** unscheduled
**Verified:** 2026-09-30 — `ls .github/workflows/` → `ci.yml`, `release-nuget.yml`, `test.yml`; no
benchmark workflow.

**Why**

[benchmarks/results/README.md](../benchmarks/results/README.md) is only updated when someone manually
runs the suite and commits the numbers. Nothing catches a PR that regresses performance between those
runs.

**Design — the constraint determines everything.** GitHub-hosted runners are far too noisy, and
differ from whoever committed the current numbers, for a committed-baseline comparison to mean
anything. **So compare PR-vs-merge-base within the same job on the same runner:** check out the
merge-base, run the suite, check out the PR head, run the same suite, diff. Two benchmark runs per PR
(~+10–20 min with `--job short`). A cross-machine comparison against committed numbers will produce
false alarms until people stop reading the output.

Compare with a `jq` step over the two `*-report-full.json` files — no extra tooling. Flag a benchmark
when `Statistics.Mean` regressed **> 10%** **and** the PR mean falls outside the base run's
`[Mean − 2·StandardError, Mean + 2·StandardError]`; or when `BytesAllocatedPerOperation` increased **at
all** for a benchmark currently at 0 B, or **> 15%** otherwise.

**Rollout:** `continue-on-error: true` plus a sticky PR comment. **Do not fail the build initially.** A
benchmark job that fails spuriously gets ignored, then disabled, then deleted — worse than not having
it. Start with `workflow_dispatch` plus nightly-against-master (cheap, catches drift, zero PR latency)
and add the `pull_request` trigger only once the suite has proven stable.

**Honest priority:** the lowest-value item in this file for a library at 1.0 with no users yet, and two
benchmark runs per PR is a real recurring cost.

**Effort:** Low.

---

### 47. CI Code Coverage Collection

**Status:** Blocked
**Type:** Infrastructure
**Target:** unscheduled
**Verified:** 2026-09-30 — `grep -n 'coverlet\|CodeCoverage' tests/Directory.Build.props`
→ `coverlet.collector 10.0.1` only.

**Why**

The test projects target `Microsoft.Testing.Platform` (MTP) via `xunit.v3`, not classic VSTest.
`coverlet.collector` — currently referenced in `tests/Directory.Build.props` — is a VSTest data
collector, so `dotnet test --collect:"XPlat Code Coverage"` is silently ignored under MTP: verified
locally, it produces zero output and no error. The MTP-native replacement,
`Microsoft.Testing.Extensions.CodeCoverage`, does work in isolation
(`dotnet test <project> -- --coverage --coverage-output-format cobertura` produces a real
`.cobertura.xml`) — but adding it as a shared package reference in `tests/Directory.Build.props` broke
`dotnet build`/`dotnet restore` entirely on this machine's installed SDK (10.0.100, resolved via
`global.json`'s `rollForward: latestMajor`) with
`NETSDK1013: The TargetFramework value '' was not recognized` — **reproduced twice**, and confirmed the
package is the cause by reverting it and rebuilding successfully. This looks like a version
incompatibility between `Microsoft.Testing.Extensions.CodeCoverage` 17.14.2 and the newest installed
SDK, not a problem with the approach.

**Fix — try the tool-based route first; it has no project-file blast radius at all:**

```bash
dotnet tool install -g dotnet-coverage
dotnet-coverage collect --output-format cobertura --output coverage.cobertura.xml "dotnet test -c Release"
```

`dotnet-coverage` attaches to the test host process and is agnostic to VSTest vs MTP. If this produces
a non-empty report, this item is solved without touching a single project file.

**If the package route is still needed, test it in isolation:**

1. Throwaway branch, never merged as-is.
2. **Do not edit `tests/Directory.Build.props`.** Add the reference to exactly one project file, so a
   bad version cannot break the other project's restore. **This is the specific mistake that produced
   the previous failure.**
3. `dotnet package search Microsoft.Testing.Extensions.CodeCoverage --take 20`. **Pin an exact
   version**; never a floating range.
4. **Remove the SDK variable from the experiment.** Test each supported SDK explicitly, without
   editing the shared `global.json` — run with `DOTNET_ROLL_FORWARD=disable` against an explicitly
   installed 8.0.404, then 9.0.x, then the newest installed, confirming `dotnet --version` each time.
5. **Success criteria, all four, on all three SDKs:** restore succeeds; `dotnet build -c Release`
   succeeds; the coverage run emits a `.cobertura.xml` with **`line-rate > 0`** — not merely a
   non-empty file, since the coverlet no-op produced no error either; and a whole-solution build still
   succeeds.
6. Only then move the reference into `tests/Directory.Build.props`, remove `coverlet.collector` as dead
   weight, and add a `coverage` job to `test.yml` uploading the report as an artifact. **No badge until
   a published report exists** — a badge without a live report behind it is an unverified claim.
7. **If it fails again**, append the exact version, SDK and error here rather than leaving a generic
   "blocked", so the next attempt doesn't repeat a known-bad combination.

**Decision:** `coverlet.collector` stays in place for now — a harmless no-op under MTP that at least
doesn't break the build.

**Unrelated cleanup to fold in:** `Microsoft.Extensions.TimeProvider.Testing` is **8.0.0** in
`tests/Resilion.Tests` and **9.0.0** in `tests/Resilion.Extensions.Tests`. Standardize on 9.0.0 in
`tests/Directory.Build.props` and drop both per-project references.

**Effort:** Medium, mostly experimentation.

---

## Retired items

Numbers that have left the active list. **Never reuse a number.**

| # | Title | Disposition |
|---|-------|-------------|
| 49 | Telemetry counters carry no dimensions/tags | Implemented — see CHANGELOG `[1.0.0-pre]`. Residual gap filed as #55. |
| 50 | ActivitySource declared but never used | Partially implemented — untyped async+sync paths only, see CHANGELOG `[1.0.0-pre]`. Residual gap filed as #56. |
| 51 | No public API surface tracking | Package wired up only; the files were not in analyzer format and nothing enforced the rules. See CHANGELOG `[1.0.0-pre]`. Actual enforcement filed as #57. |
| 46 | Timeout cancellation TOCTOU race | Fixed — atomic first-cause recording. See CHANGELOG `[Unreleased]`. |
| 52 | No SourceLink, symbol packages, or deterministic builds | Done — SourceLink, .snupkg and deterministic CI builds. See CHANGELOG `[Unreleased]`. |
| 53 | No SECURITY.md | Done — `SECURITY.md` added. Requires Private Vulnerability Reporting to stay enabled. |
| 55 | Hedging sync path never increments hedging.attempts | Fixed. See CHANGELOG `[Unreleased]`. |
| 56 | Typed strategies emit no ActivitySource spans | Fixed — spans extracted to `StrategyActivity` and applied to all entry points. |
| 57 | PublicAPI files not in analyzer format; no CI enforcement | Fixed — files regenerated, RS0016/RS0017 are errors in CI. |
| 58 | telemetry.md documents span tags that don't exist | Fixed — documented tag set now matches the code. |
| 59 | Hedging drops PipelineName on per-attempt contexts | Fixed. See CHANGELOG `[Unreleased]`. |
| 60 | Typed-strategy result mismatch silently skips the strategy | Fixed — now throws `InvalidOperationException`. |
| 61 | README's synchronous-execution claims are broader than the truth | Fixed — claim scoped, four exceptions documented. |

---

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they
won't be fixed, and [verification.md](verification.md) — the commands that back every factual claim in
this file.
