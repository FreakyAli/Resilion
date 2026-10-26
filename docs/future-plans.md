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
| 63 | Outcome-aware retry delay (blocks Retry-After) | P1 | Feature | post-1.0 | Medium | Open |
| 4 | Per-call delegate allocation in pipeline chain | P1 | Improvement | post-1.0 | High | Open |
| 64 | Composable event handlers | P2 | Improvement | unscheduled | Medium | Open |
| 65 | Registry factory with IServiceProvider | P2 | Improvement | unscheduled | Low | Open |
| 66 | Synchronous ExecuteOutcome | P3 | Feature | unscheduled | Medium | Open |
| 27 | Retry typed/untyped strategy duplication (4 loops) | P1 | Improvement | post-1.0 | Medium | Open |
| 70 | FallbackAction Task.Run missing CancellationToken | P1 | Fix | post-1.0 | Low | Open |
| 71 | AddResiliencePipeline<TOptions> emits IL2091 under Native AOT | P2 | Improvement | post-1.0 | Low | Open |
| 9 | Synchronous delays bypass TimeProvider (untestable) | P1 | Fix | post-1.0 | Low | Open |
| 54 | Evaluate multi-targeting net9.0 for System.Threading.Lock | P1 | Improvement | post-1.0 | Medium | Open |
| 45 | Telemetry enrichment (MeteringEnricher, listeners) | P2 | Feature | unscheduled | High | Open |
| 68 | Chaos strategy options not bindable from IConfiguration | P2 | Feature | unscheduled | Low | Open |
| 69 | No chaos injection on the standard HTTP handler | P3 | Feature | unscheduled | Medium | Open |
| 67 | Registry caches a failed pipeline build until invalidated | P2 | Improvement | unscheduled | Low | Open |
| 11 | Hedging: O(N²) WhenAny continuation registration (needs #54) | P2 | Improvement | unscheduled | Medium | Open |
| 5 | CancellationTokenSource pooling for Timeout | P2 | Improvement | unscheduled | Medium | Open |
| 48 | Benchmark CI on PRs | P3 | Infrastructure | unscheduled | Low | Open |
| 47 | CI code coverage collection | P3 | Infrastructure | unscheduled | Medium | **Blocked** |

---

## P0 — Must land before 1.0.0

_Empty._ Everything that was blocking 1.0.0 has shipped — see `## Retired items` and
[CHANGELOG.md](../CHANGELOG.md). A new entry belongs here only if it makes something the library
already claims untrue, or produces a silently wrong answer.

## P1 — Next (post-1.0)


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



### 70. `FallbackAction` `Task.Run` Missing `CancellationToken`

**Status:** Open
**Type:** Fix
**Target:** post-1.0
**Verified:** 2026-10-24 — `grep -rn 'Task.Run' src/Resilion/Fallback/FallbackAction.cs
src/Resilion/ResilienceEventHandler.cs` → one call site, `FallbackAction.cs:64`, passing no token.
The only hit in `ResilienceEventHandler.cs` is prose in a doc comment.

**Why**

This is the surviving half of retired #30. `FallbackAction<T>.Execute` calls
`Task.Run(() => factory(context).AsTask()).GetAwaiter().GetResult()` for sync-over-async without
passing a `CancellationToken`. The other half — the same pattern in
`ResilienceEventHandler<T>.Invoke` — disappeared when that type became a delegate (#62): its
replacement clears the `SynchronizationContext` and runs the handler on the calling thread instead
of dispatching to the pool, so there is no `Task.Run` left to pass a token to.

**Be accurate about the benefit.** `Task.Run(f, ct)` only declines to *schedule* work when `ct` is
already cancelled. It cannot interrupt a running factory, and the surrounding
`.GetAwaiter().GetResult()` blocks the calling thread regardless. Correct hygiene, not a
cancellation fix.

**Fix**

`FallbackContext<TResult>` already carries `Context.CancellationToken`, so `Execute` needs no
signature change — pass it to `Task.Run`. **Public API: none.**

Worth considering at the same time: whether `FallbackAction` should adopt #62's approach and clear
the `SynchronizationContext` rather than dispatching to the pool, which would remove the thread hop
for a synchronously-completing fallback factory exactly as it did for handlers. That is the larger
and more useful change of the two.

**A test must force a `SynchronizationContext`** to reach the `Task.Run` branch at all — it is
skipped when `SynchronizationContext.Current is null`, which is the default under xunit. See
`ResilienceEventHandlerTests`' `DeadEndSynchronizationContext` for the pattern.

**Effort:** Low.

---

### 9. Synchronous Delays Bypass `TimeProvider` (Untestable)

**Status:** Open
**Type:** Fix
**Target:** post-1.0
**Verified:** 2026-10-23 — `grep -rn 'WaitHandle.WaitOne' src/ --include='*.cs' | grep -v /obj/`
→ three sites: `src/Resilion/Retry/RetryStrategy.cs:143`, `:272`, and
`src/Resilion.Chaos/ChaosLatencyStrategy.cs:56`.

**Why**

This entry previously claimed that `CancellationToken.WaitHandle` "lazily allocates a
`ManualResetEvent` that is never explicitly disposed … on every retry delay". **That is wrong.** The
handle is allocated **once per `CancellationTokenSource`**, is disposed with the CTS, and for a
default (`CanBeCanceled == false`) token the runtime uses a static never-signalled event with no
allocation at all. Across a retry loop the cost amortises to near zero, and the obvious alternatives
are worse: `Thread.Sleep` loses cancellation responsiveness, spin-polling burns CPU.

**The real defect** is that every synchronous delay in the library waits via
`WaitHandle.WaitOne(delay)`, **bypassing the configured `TimeProvider` entirely**.
`builder.TimeProvider` is this repo's single time-injection seam — it is how every other timing test
uses `FakeTimeProvider`. So **no synchronous delay can be driven by a fake clock, and no sync
timing test can be deterministic.**

It was previously a retry-only problem. `Resilion.Chaos`'s latency strategy has the same shape and
therefore inherits it: `ChaosLatencyStrategy.ExecuteAsync` delays through `_timeProvider` and is
fully `FakeTimeProvider`-driven, while `Execute` cannot be. `ChaosLatencyStrategyTests` works
around it with a short real delay and a comment pointing here.

**Each new synchronous delay added to the library adds a site**, so the fix gets slightly more
valuable each time rather than less.

**Fix**

Needs a sync-waitable abstraction over `TimeProvider` — something that can block the calling thread
for a provider-controlled duration while remaining cancellation-responsive. That is a design change,
not a patch, which is why this is not a 1.0 item. Do it when someone actually needs deterministic
sync timing coverage, or when a third site appears.

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

### 63. Outcome-Aware Retry Delay

**Status:** Open
**Type:** Feature
**Target:** post-1.0
**Verified:** 2026-10-23 — `RetryDelayContext` carries `AttemptNumber` only; no overload receives
the outcome.

**Why**

`RetryDelay.Custom` receives `RetryDelayContext(AttemptNumber)`, so a delay cannot depend on what
came back. That makes honouring a `Retry-After` header impossible, and `OnRetry` cannot help —
it observes the attempt but cannot change the delay.

This is the largest remaining functional gap against `Microsoft.Extensions.Http.Resilience`, and it
bites by default: both libraries retry 429 out of the box, and Resilion does so on blind
exponential backoff while the server is telling it exactly how long to wait.

**Fix:** give the delay context the outcome — `RetryDelayContext<TResult>` carrying
`Outcome<TResult>` alongside the attempt number. `Resilion.Http` can then map `Retry-After` to a
delay without the core knowing anything about HTTP.

---




### 4. Per-Call Delegate Allocation in Pipeline Chain

**Status:** Open
**Type:** Improvement
**Target:** post-1.0
**Verified:** 2026-10-23 — `benchmarks/results/Resilion.Benchmarks.HttpResilienceHandlerBenchmarks-report-github.md`,
full run, not ShortRun.

**Why**

`StrategyComponent.ExecuteAsync` creates the closure `ctx => _next.ExecuteAsync(callback, ctx)` per
execution, so an N-strategy pipeline allocates N closures per call.

**This is now the largest measured gap against Polly**, which is why it has moved from "accepted
tradeoff, revisit if profiling shows it matters" to a priority. `Resilion.Http` was the workload
the earlier write-up predicted would surface it:

| | Total alloc | Over a bare client | Gen0 per 10k ops |
|---|---:|---:|---:|
| Bare `HttpClient` | 960 B | — | 1.14 |
| Polly standard handler | 1,224 B | **264 B** | 1.45 |
| Resilion standard handler | 2,624 B | **1,664 B** | 3.13 |

The headline "2.1× Polly" understates it. The marginal cost of the resilience layer — what the
library actually adds — is **6.3×** Polly's. A faint Gen1 collection appears on Resilion's retry
arm and none on Polly's.

**The previous decision rule was broken. Do not use it.**

It said: act if "Gen0 collections exceed 1 per 10,000 operations". Measured against a baseline,
that threshold fires on a **bare `HttpClient` with no resilience library at all** (1.14), and on
Polly (1.45). A criterion that condemns the control group measures nothing, and running the
benchmark under it would have produced a guaranteed "yes, redesign" carrying no information.

**Corrected decision rule — relative to a control, not absolute.** Pursue the pre-composed
delegate chain only if, at ~1 ms simulated per-call latency:

1. **Throughput:** Resilion's handler is **>= 3% slower** than Polly's on the same pipeline shape.
   Today Resilion is *faster* at zero latency, so this asks whether allocation reverses that once
   real work exists.
2. **GC pressure:** Resilion's Gen0 rate exceeds the **bare-client baseline** by more than **2×**.
   Today it is 2.7× (3.13 vs 1.14), so this would currently trigger — and meaningfully, because
   the baseline is subtracted out.
3. **Promotion:** any sustained Gen1 or Gen2 activity attributable to the pipeline. Per-call
   closures should die in Gen0; promotion means something is surviving and is worth understanding
   whatever the throughput numbers say.

**Open question before writing the benchmark: should it be concurrent?** Every benchmark in this
repo is single-threaded, and allocation pressure is a concurrent phenomenon — one thread allocating
1.6 KB/ms is nothing, two hundred in-flight requests doing the same is ~320 MB/s. Single-threaded
measurement may be the reason this item looked harmless for as long as it did.

**Note on scope.** The fix "requires complex generic type threading and may not be feasible without
sacrificing API simplicity" — in a library whose pitch is simplicity, that remains close to
disqualifying. The realistic outcome may be to accept the cost with the numbers documented, rather
than to redesign. Deciding that honestly still needs the benchmark above.

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
`ILogger` bridge, which means re-adding the `Microsoft.Extensions.Logging.Abstractions` reference
to `Resilion.Extensions`. It was removed before 1.0 rather than shipped dead: it had no
references in any source file, so it made consumers restore a package for nothing and implied a
logging integration that did not exist.

**Be honest about severity:** core has no logger, so severity is only meaningful for suppression via
`None` and for user listeners. Say that rather than implying log-level integration.

**Hard prerequisite:** ship #56 first. Enrichment layered on top of partial span coverage gives users
enrichment that silently doesn't apply on typed pipelines — worse than no enrichment.

**Effort:** High. This is the most invasive item in the backlog: every emit site in every strategy.

---


### 67. Registry Caches a Failed Pipeline Build Until Invalidated

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Verified:** 2026-10-23 — a probe registering a factory that throws on its first two builds, then
resolving five times with invalidations in between. Builds 1–3 all rethrew *the same* exception
instance from build 1 (`builds` counter stayed at 1); only `InvalidatePipeline` re-ran the factory.

**Why**

`ResiliencePipelineRegistry.GetPipeline` caches each pipeline in a `Lazy<Pipeline>` built with the
default `LazyThreadSafetyMode.ExecutionAndPublication`, which **caches the exception** when the
factory throws. So a `configure` delegate that fails — bad values from a reload, a dependency that
was not ready at first resolve — leaves that key permanently broken: every later `GetPipeline`
rethrows the stored exception without re-running the factory.

The lookup guard above the `Lazy` only covers an *unknown key*; it does nothing for a factory that
throws.

With reload wired up this self-heals, because the next options change invalidates the key. Without
it, the key is broken for the process lifetime. The failure is also confusing to diagnose: the
exception's stack trace points at the first resolve, which may be long past.

**Current mitigation:** documented in [dynamic-reload.md](dynamic-reload.md), and
`InvalidatePipeline` returns `true` for a cached failure, so a caller can clear one deliberately.

**Possible fixes**

1. **Do not cache the failure** — catch inside the `Lazy` factory, remove the entry from
   `_pipelines`, rethrow. The next resolve then retries. Risk: a factory that always throws is now
   re-run on every call, which under load turns one cached exception into a hot loop of failing
   builds.
2. **`LazyThreadSafetyMode.PublicationOnly`**, which does not cache exceptions. Same retry-storm
   risk, and it permits concurrent factory execution, so two callers could build two pipelines and
   one gets discarded — unacceptable while a pipeline may own a circuit breaker.
3. **Keep the caching, add a documented escape hatch.** Current state, plus the `InvalidatePipeline`
   route above.

(1) with a short backoff is the only one that improves on today without a new hazard, and it is not
obviously worth the machinery. Decide only if someone hits it.


### 68. Chaos Strategy Options Are Not Bindable from `IConfiguration`

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Verified:** 2026-10-23 — `ls src/Resilion.Extensions/Configuration/` → five DTOs covering retry,
retry delay, timeout, circuit breaker and hedging. No chaos DTO exists.

**Why**

`Resilion.Extensions.Configuration` makes strategy options bindable from `appsettings.json`, and
chaos is arguably the strategy that most wants it. Its two primary knobs are exactly the kind of
thing an operator tunes per environment without a deploy:

```json
{ "Chaos": { "Fault": { "Enabled": true, "InjectionRate": 0.05 } } }
```

Today both have to be written in code, so turning injection up during a game day — or off during an
incident — means a rebuild. Combined with #41's reload, a bindable DTO would make the injection
rate adjustable at runtime, which is the natural way to run a chaos experiment.

The shape follows the existing DTOs exactly: mutable POCOs with parameterless constructors and a
`ToOptions()` projection, with the delegate members (`EnabledGenerator`, `Randomizer`,
`FaultGenerator`, `OutcomeGenerator`, `LatencyGenerator`, `Behavior`) left off and layered on
afterwards with a record `with` expression.

**One design question to settle first.** The production guard documented in `chaos.md` is
`EnabledGenerator = _ => !env.IsProduction()`, a delegate — so it cannot come from configuration,
and a bindable `Enabled = true` sitting in a config file is a loaded gun pointed at production. A
chaos DTO should probably **not** expose `Enabled` at all, leaving the kill switch in code and
making only the rate configurable. Decide that before writing the type.

Blocked on nothing; it is additive. `ChaosBehaviorStrategyOptions` is the one that cannot be
usefully bound, since `Behavior` is required and is a delegate.

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

### 64. Composable Event Handlers

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Verified:** 2026-10-23 — `grep -n 'internal.*Invoke' src/Resilion/ResilienceEventHandler.cs`.

**Why**

`ResilienceEventHandler<TArgs>`'s invocation is internal, so an out-of-package strategy or handler
can only *replace* a user's `OnRetry`, never wrap it. `Resilion.Http` wanted to hook retry for
response disposal and could not, which is why it carries its own attempt tracker instead.

Any future package wanting to observe a user-supplied callback hits the same wall.

---

---

### 65. Registry Factory with IServiceProvider

**Status:** Open
**Type:** Improvement
**Target:** unscheduled
**Verified:** 2026-10-23 — `ResiliencePipelineRegistry.RegisterPipeline` takes
`Action<PipelineBuilder<TResult>>` with no provider.

**Why**

A pipeline built lazily by the registry cannot resolve services, because the factory signature
carries no `IServiceProvider`. `Resilion.Http` works around it with a small accessor the handler
factory fills immediately before resolving the pipeline — deterministic, but indirection that an
`Action<PipelineBuilder<TResult>, IServiceProvider>` overload would delete outright.

---

---

### 71. `AddResiliencePipeline<TOptions>` Emits Trim Warnings Under Native AOT

**Status:** Open
**Type:** Improvement
**Target:** post-1.0
**Verified:** 2026-10-25 — `dotnet publish samples/Resilion.Samples/Resilion.Samples.csproj -c Release
-r osx-arm64 -p:PublishAot=true` → two `IL2091` warnings, at
`ResilionServiceCollectionExtensions.cs:367` and `:402`.

**Why**

`MonitoredPipelineConfigurator<TOptions>` and `MonitoredTypedPipelineConfigurator<TResult, TOptions>`
pass their `TOptions` to `IOptionsMonitor<TOptions>`, which annotates its type parameter with
`DynamicallyAccessedMemberTypes.PublicParameterlessConstructor`. Ours carries no matching
annotation, so the trimmer cannot prove the constructor survives.

**This does not fail CI**, because `IL2091` is not in the `/warnaserror` list `test.yml` uses
(`IL2026,IL2070,IL2072,IL3050,IL3053`), and the AOT publish succeeds. It is also consistent with
`Resilion.Extensions` declaring `IsAotCompatible=false` — the package does not claim to be
trim-safe. But an application that AOT-publishes while using the reload overloads sees two
warnings pointing into this library, which is a poor first impression.

Introduced with the reload overloads in #41 (commit `c4437b4`), not by anything later.

**Fix**

Annotate the two type parameters:

```csharp
internal sealed class MonitoredPipelineConfigurator<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
```

and propagate the annotation up through the four public `AddResiliencePipeline<TOptions>` overloads.

**Why it was not done before 1.0:** the annotation on a public generic parameter propagates the
requirement to callers, so a caller with its own generic wrapper would start seeing the warning at
its call site. That is source-compatible and can be added in any later version without breaking
anyone, so it did not need to beat the API freeze. Doing it reflexively minutes before the tag was
the worse option.

**Effort:** Low.

---

---

## P3 — Blocked / needs external change

### 69. No Chaos Injection on the Standard HTTP Handler

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Verified:** 2026-10-23 — `grep -n 'Chaos' src/Resilion.Http/*.cs src/Resilion.Http/Internal/*.cs`
→ no matches. `HttpStandardResilienceOptions` exposes `RateLimiter`, `TotalRequestTimeout`,
`Retry`, `CircuitBreaker`, `AttemptTimeout` and `RequestReplay`, with no chaos hook.

**Why**

`AddStandardResilienceHandler` builds a fixed five-strategy pipeline, so there is no way to inject
chaos into it. That is the pipeline most users will actually run, and therefore the one most worth
testing — "does my standard handler really retry a 503?" is answerable only by making a 503 happen.

**There is a real workaround, which is why this is unscheduled rather than P1:**
`AddResilienceHandler(key, builder => …)` takes a pipeline you compose yourself, so you can
reproduce the standard shape and add chaos innermost. The cost is that you now maintain a copy of
the standard pipeline's defaults, which will drift from ours.

**Options**

1. **A `Chaos` property on `HttpStandardResilienceOptions`**, appended innermost when set. Smallest
   surface, but it puts a `Resilion.Chaos` type on a `Resilion.Http` options class, which would
   make `Resilion.Http` depend on `Resilion.Chaos` — a chaos dependency for every HTTP consumer.
   Probably disqualifying on its own.
2. **A separate `AddStandardResilienceHandlerWithChaos` in a `Resilion.Http.Chaos` package**, which
   depends on both and keeps the base package clean. More packaging for a testing-only feature.
3. **Expose the standard pipeline's configuration as a reusable builder action**, so a caller can
   compose it with their own additions without copying the defaults. Solves the drift problem
   generally, not just for chaos, and adds no cross-package dependency.

(3) looks best and is the only one that helps anyone not doing chaos. Do not start any of them
without deciding which.

---

---

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

**Effort:** Medium, mostly experimentation.

---

### 66. Synchronous ExecuteOutcome

**Status:** Open
**Type:** Feature
**Target:** unscheduled
**Verified:** 2026-10-23 — `Pipeline<TResult>` exposes `ExecuteOutcomeAsync` but no sync equivalent.

**Why**

`ExecuteOutcomeAsync` is what lets a caller see the winning outcome without it being thrown, which
is how `Resilion.Http` identifies the response to keep and disposes the rest. There is no
synchronous counterpart, so `HttpClient.Send` cannot be supported without either leaking responses
or behaving differently from the async path — it throws `NotSupportedException` instead.

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
| 44 | `IConfiguration` binding for strategy options | Done — five bindable DTOs in `Resilion.Extensions.Configuration`. See CHANGELOG `[Unreleased]` and [configuration.md](configuration.md). |
| 40 | `Resilion.Chaos` — chaos engineering | Done — fault, outcome, latency and behavior injection, plus a not-innermost ordering warning. See CHANGELOG `[Unreleased]` and [chaos.md](chaos.md). |
| 62 | Event handler properties reject a bare lambda | Fixed — `ResilienceEventHandler<TArgs>` is now a delegate returning `ValueTask`, so lambdas and method groups convert. See CHANGELOG `[Unreleased]`. |
| 30 | FallbackAction/ResilienceEventHandler `Task.Run` missing token | Half closed by #62, which removed the `ResilienceEventHandler` call site entirely. Remainder filed as #70. |
| 61 | README's synchronous-execution claims are broader than the truth | Fixed — claim scoped, four exceptions documented. |
| 39 | Resilion.Http — HttpClient integration | Implemented. See CHANGELOG `[Unreleased]`. Residual gaps filed as #63–#66. |
| 10 | Resilion.Testing package | Deferred; the two core gaps behind it (`CircuitBreakerStateProvider`, public `Pipeline.Name`) shipped instead. See CHANGELOG `[Unreleased]`. |
| 12 | Hedging: HedgingDelayGenerator | Implemented. See CHANGELOG `[Unreleased]`. |
| 41 | Pipeline dynamic reload via IOptionsMonitor | Implemented. See CHANGELOG `[Unreleased]`. |
| 42 | Keyed services support | Implemented — always on, with idempotent `Pipeline.Dispose` as its prerequisite. |
| 43 | PredicateBuilder<T> fluent API | Implemented — generic and non-generic. See CHANGELOG `[Unreleased]`. |

---

See also: [tradeoffs.md](tradeoffs.md) — accepted design imperfections with reasoning for why they
won't be fixed, and [verification.md](verification.md) — the commands that back every factual claim in
this file.
