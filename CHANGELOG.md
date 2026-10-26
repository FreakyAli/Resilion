# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_No changes yet._

## [1.0.0] - 2026-10-25

### Added

- **`Resilion.Http` — `HttpClient` and `IHttpClientFactory` integration.** The most common use for
  a resilience library, and the one gap `comparison-with-polly.md` told readers to use Polly for.
  `AddStandardResilienceHandler()` on `IHttpClientBuilder` gives rate limiting, a total timeout,
  retry, a circuit breaker and a per-attempt timeout, retrying 5xx, 408, 429,
  `HttpRequestException` and `TimeoutRejectedException`. `AddStandardHedgingHandler()` swaps retry
  for hedging; `AddResilienceHandler(key, …)` takes a pipeline you build. Also
  `HttpResiliencePredicates`, `HttpRequestReplay`, and `ResilienceContext.GetRequestMessage()`.
  See [docs/http.md](docs/http.md)
  - **The pipeline is built once and cached in the registry, never in the handler factory.** That
    factory re-runs on every `IHttpClientFactory` handler rotation — two minutes by default — so a
    pipeline built there would silently reset the circuit breaker's window on each one, leaving a
    breaker that can essentially never trip, with no symptom a user could attribute to this
    library. Pinned by a test that fails if the pipeline stops being cached
  - **Responses from losing attempts are disposed.** The pipeline discards non-final outcomes
    without disposing them, and an undisposed response holds its connection, so without this a
    retry-heavy client exhausts its pool. Disposal is eager where attempts cannot overlap, and
    covers hedged stragglers that answer after the handler has already returned
  - **`HttpRequestReplay`** chooses whether each attempt sends your request or an independent
    clone. `ReuseRequest` (default) matches Polly and costs nothing; `CloneRequest` buffers the
    body once and is required for non-replayable content or header-mutating inner handlers. The
    hedging handler always clones, since concurrent attempts cannot share one request
  - **Rate limiting is off by default**, unlike `Microsoft.Extensions.Http.Resilience` 8.x (9.x
    also removed it). A `RateLimiter`'s lifetime belongs to its creator, so enabling one by default
    would mean this package silently owning a limiter and throttling any application above its
    permit count
  - **`HttpClient.Send` throws `NotSupportedException`.** `DelegatingHandler.Send` forwards
    straight to the inner handler, so without the override a synchronous caller would bypass the
    entire pipeline and believe it was protected
  - Known limitations, all documented: no `Retry-After` honouring, no synchronous send, no
    per-endpoint hedging routing. Tracked as #63 and #66
- Benchmarks for the HTTP handler against `Microsoft.Extensions.Http.Resilience` 8.10.0 through a
  real `IHttpClientFactory` graph, at full iteration count rather than `ShortRun` because three
  iterations could not separate the two libraries. Resilion is 2.3% faster on the happy path and
  10.6% faster with a retry — both real, both irrelevant beside real I/O — while allocating 2.1×
  the total, or 6.3× the marginal cost over a bare client. Both arms were equalised deliberately — five strategies
  each, retry present in both (Polly validates `MaxRetryAttempts >= 1`, Resilion permits 0, and
  disabling it would have compared four strategies against five), and a breaker present but with
  its threshold raised so a benchmark's sustained failure rate cannot trip it

- `PredicateBuilder<TResult>` and `PredicateBuilder` — compose a strategy's `ShouldHandle` from
  `Handle<TException>()`, `HandleInner<TException>()` and `HandleResult(...)` clauses instead of
  writing the delegate by hand, with an implicit conversion so a builder can be assigned directly.
  Clauses combine with OR. Two deliberate behaviours: `Build()` **throws on an empty builder**, so
  it can never become a second route to the default predicate (reachable only by leaving
  `ShouldHandle` null); and it does **not** inherit the default's cancellation carve-out, so
  `.Handle<Exception>()` matches `OperationCanceledException`. Unlike `PipelineBuilder` it is not
  single-use — it produces a value rather than configuring one thing
- `HedgingStrategyOptions<TResult>.HedgingDelayGenerator` and `HedgingDelayGeneratorArgs` — compute
  the delay before each hedged attempt. Because the delay *is* the mode selector, a generator
  selects the mode **per attempt**: `InfiniteTimeSpan` makes that attempt wait for its predecessor,
  `Zero` launches it immediately. `AttemptNumber` is the attempt being launched, so `1` for the
  first hedge, matching the existing hedging args records. Unsupported on the synchronous path,
  which throws, as it already does for parallel and latency modes
- `CircuitBreakerStateProvider`, plus `StateProvider` on both circuit breaker options records —
  the first public way to read a circuit's state. Previously `CircuitBreakerManualControl` could
  isolate and reset a circuit but not report it, forcing callers into indirect assertions like
  "the next call threw `CircuitBrokenException`, therefore it must be open". Bind one instance per
  breaker; a second binding throws. **Do not branch on it** — besides being a point-in-time read,
  gating execution on it skips the half-open trial call and can leave a circuit open after the
  dependency recovers. Documented on the property and in `docs/circuit-breaker.md`
- `ResiliencePipelineRegistry<TKey>.InvalidatePipeline(key)` and `InvalidatePipeline<TResult>(key)`,
  with an optional `OnPipelineReplaced` callback and `PipelineReplacedArgs<TKey>` — drop a cached
  pipeline so the next resolve rebuilds it. The registry never disposes a replaced pipeline,
  because it cannot know when in-flight executions on it have finished; the callback is how the
  caller learns there is something to decide about
- `AddResiliencePipeline<TOptions>` / `<TResult, TOptions>` overloads (with and without a named
  options instance) — bind a pipeline to `IOptionsMonitor<TOptions>` and rebuild it automatically
  when those options change. This is what finally uses the `Microsoft.Extensions.Options`
  reference that had been declared but unused since the package was created. **Resolve such a
  pipeline per call** through `IPipelineProvider<TKey>`; a consumer that caches it in a field keeps
  executing the old one
- `ConfigureResiliencePipelineRegistry` — configure the registry itself. Runs before any pipeline
  factory is registered, so registry-level settings are in place before a pipeline can be built
- `ResiliencePipelineRegistry<TKey>.TryGetPipeline<TResult>` — fills the asymmetry with the
  existing non-generic `TryGetPipeline`. Deliberately not added to `IPipelineProvider<TKey>`, since
  adding a member to a public interface breaks external implementers; that waits for a major version
- Keyed DI resolution of named pipelines: `[FromKeyedServices("name")] Pipeline` now works, with no
  new Resilion API — the consumer-facing surface is the BCL attribute. Registered for every
  `AddResiliencePipeline` overload with `TryAddKeyedSingleton`, so a duplicate call does not add a
  second descriptor. **Incompatible with reload by construction** (a keyed singleton resolves once
  for the container's lifetime), which is documented on both sides and pinned by a test
- **`Resilion.Chaos` — fault, outcome, latency and behavior injection.** Verify that your
  resilience configuration actually does something, by making the failures happen on purpose.
  `AddChaosFault`, `AddChaosOutcome`, `AddChaosLatency` and `AddChaosBehavior` on both builders.
  Zero third-party dependencies — it references `Resilion` and nothing else.
  See [docs/chaos.md](docs/chaos.md)
  - **Chaos must be innermost, and the pipeline warns when it isn't.** A chaos strategy outside a
    retry injects its fault before the retry can observe it, so the retry never sees a failure and
    the experiment passes while proving nothing. That is the worst possible failure for a chaos
    run — silent and positive — so it is diagnosed rather than left to be discovered. A warning
    rather than an error, because stacking several chaos strategies at the end is legitimate
  - **`Randomizer` is the supported test seam**, not polish: injection is a coin flip, so without
    it every assertion about chaos would be statistical. `() => 0.0` always injects, `() => 1.0`
    never does, and the randomizer is consulted on every decision with no short-circuit so those
    hold at any configured rate
  - `AddChaosLatency` takes `builder.TimeProvider`, so `FakeTimeProvider` drives injected delays.
    The synchronous path waits on `CancellationToken.WaitHandle` and cannot be driven by a fake
    clock — the same limitation as synchronous retry delays, documented on both
  - **Generators always take precedence over their static counterparts**, decided in one shared
    place so the four strategies cannot drift apart. A bad static rate throws at `Add*` time where
    the stack trace points at your configuration; a generator returning an out-of-range value is
    clamped, because it runs mid-execution where throwing would be indistinguishable from an
    injected fault
  - **`AddChaosOutcome` is typed-only on purpose.** A `Strategy<TResult>` cannot run in a pipeline
    of a different result type, so an untyped overload would be a trap with no correct use
  - Injections increment `resilion.chaos.injections` on the existing `Resilion` meter — one
    `.AddMeter("Resilion")` still covers everything — tagged with the new public
    `ResilionTelemetry.ChaosStrategyTag`. The counter counts injections, not decisions: a
    `FaultGenerator` returning `null` skips injection and does not increment
  - **The defaults inject.** `Enabled` is `true` and `InjectionRate` is `0.001`. The docs lead with
    `EnabledGenerator = _ => !env.IsProduction()` rather than mentioning it in passing

- `Resilion.Extensions.Configuration` — five bindable DTOs (`RetryStrategyConfiguration`,
  `RetryDelayConfiguration`, `TimeoutStrategyConfiguration`, `CircuitBreakerStrategyConfiguration`,
  `HedgingStrategyConfiguration`) that project into the real options records with `ToOptions()`.
  The options records are `sealed record` types with `init`-only members and `RetryDelay` is a
  closed union with a private constructor, so neither is bindable — and making the union bindable
  would reintroduce exactly the contradictory-delay-properties ambiguity it exists to prevent.
  See [docs/configuration.md](docs/configuration.md)
  - **Every DTO default mirrors its options record exactly**, so binding nothing is identical to
    passing no options. Drift would be a silent behaviour change, so each pair is asserted field by
    field
  - **The delay cap lives on the strategy, not the delay.** `RetryStrategyConfiguration.MaxDelay`
    applies to every backoff kind including `Constant`, whose factory takes no maximum at all;
    a second cap on `RetryDelayConfiguration` would be two knobs for one thing
  - **`RetryDelayKind` has no `Custom` member** and the DTOs carry no delegate properties —
    `ShouldHandle`, `OnRetry` and the generators cannot come from a file, and a property that
    silently does nothing when bound is worse than no property. Layer them on with a record `with`
    expression, which needs no extra API
  - Whole pipelines from JSON remain deliberately out of scope: it would make `OrderingValidator`'s
    guarantees a property of a configuration file while still requiring C# for every predicate
- `docs/dynamic-reload.md` and `docs/configuration.md`. Reload and keyed DI shipped above with
  their contracts recorded only in this changelog, which is not documentation — a reader of `docs/`
  had no way to learn that a reloadable pipeline must be resolved per call, or that keyed injection
  never observes a reload. Both rules are now written down where they are looked for

### Changed
- **`ResilienceEventHandler<TArgs>` is now a delegate returning `ValueTask`, not a wrapper
  struct.** The obvious way to write a callback now compiles:

  ```csharp
  OnRetry = e => { logger.LogWarning("Retry {Attempt}", e.AttemptNumber); return default; }
  OnRetry = LogRetry;                          // method groups too
  OnRetry = async e => await TrackAsync(e);    // and async
  ```

  The struct held either an `Action<TArgs>` or a `Func<TArgs, ValueTask>` behind implicit
  conversions, which let a synchronous handler skip the `return default`. But a struct is not a
  delegate type, and C# will not convert a lambda to one, so **every** callback had to be written
  `new Action<RetryAttemptEvent>(e => ...)` — a bare lambda failed with `CS1660` and a method
  group with `CS0428`. The README's own example, and the type's own XML doc example, were both
  uncompilable
  - **The justification for the wrapper did not survive measurement.** It was documented as
    avoiding `ValueTask` wrapping for synchronous callbacks. A synchronous handler returning
    `default` allocates **0 B/op**, exactly as an `Action<T>` does, because `ValueTask` is a
    struct; only a handler that genuinely suspends allocates, and the wrapper paid that too. It
    was buying syntax, not speed, and charging a compiler error for it
  - **The synchronous path got faster under a `SynchronizationContext`.** It used to push async
    handlers onto the thread pool via `Task.Run` to avoid the classic deadlock. It now clears the
    context for the duration of the call instead, so the continuation resumes on the pool while
    the handler itself still starts on the calling thread — which means a **synchronous** handler
    no longer hops threads at all in WPF, WinForms or legacy ASP.NET. Both behaviours are pinned
    by tests that fail if the context is not cleared or if the old `Task.Run` returns
  - This removes `ResilienceEventHandler<TArgs>.HasHandler` and its two implicit conversion
    operators from the public surface. The 17 callback properties keep their spelling —
    `ResilienceEventHandler<TArgs>?` — but the `?` now means a nullable reference rather than
    `Nullable<T>` over a struct, so `.HasValue` and `.Value` no longer apply. Pattern matching
    (`is { } handler`) works unchanged either way
- `Resilion.Extensions` no longer references `Microsoft.Extensions.Logging.Abstractions`. It had
  been declared since the package was created and used by nothing — no `ILogger` in any source
  file — so consumers restored it for nothing and the dependency implied a logging integration
  that did not exist. It returns when telemetry enrichment ships an `ILogger` bridge (#45)
- The test projects no longer reference `coverlet.collector` or `xunit.runner.visualstudio`. Both
  are VSTest-era packages and this repo runs Microsoft.Testing.Platform, under which they do
  nothing; verified by removing them and confirming all 430 tests still discover, run and emit TRX
- `Microsoft.Extensions.TimeProvider.Testing` moved to `tests/Directory.Build.props` at a single
  version. It had drifted to 8.0.0 in one test project and 9.0.0 in the others, so the fake clock
  under test was not the same one everywhere
- README and `docs/installation.md` no longer describe `Resilion.Extensions` as providing
  "structured logging", and the dependency graph now lists the real per-package dependencies and
  includes `Resilion.Http`. There has never been an `ILogger` anywhere in the library

- `Pipeline.Name` and `Pipeline<TResult>.Name` are public. Useful for logging on its own, and it
  removes the only `InternalsVisibleTo` a future testing package would have needed
- `Pipeline.Dispose`/`DisposeAsync` and the typed equivalents are **idempotent**; the second and
  later calls are no-ops. Required by keyed DI, where the container disposes the keyed singleton
  *and* the registry disposes every pipeline it created, so the same instance is reached twice.
  Harmless while no strategy holds a resource, and a real double-dispose bug the day one does
- **`README.md`'s callback example did not compile.** `OnRetry = ctx => { … }` fails with CS1660,
  and a method group fails with CS0428: `ResilienceEventHandler<TArgs>` converts implicitly from
  `Action<TArgs>` but is not itself a delegate type, so a lambda has no target type to bind to. The
  example now compiles and states the constraint, and `comparison-with-polly.md` keeps the
  performance claim (a sync handler runs inline, with no `ValueTask` allocation) while conceding
  brevity to Polly's plain-delegate properties. Tracked for a real fix as #62
- `docs/testing.md` asserts circuit state directly with `CircuitBreakerStateProvider` instead of
  teaching the indirect "it threw, so it must be open" idiom, with a pointer to why that idiom
  must not be lifted into production code

### Fixed
- **`CloneRequest` handed retry callbacks a disposed request.** The per-attempt clone is disposed
  as soon as the attempt returns, but strategy callbacks run after that — so `OnRetry` calling
  `context.GetRequestMessage()` got a disposed message and threw on touching its content. The
  context now points back at the caller's live request before the clone is disposed
- **The HTTP replay mode was re-resolved on every handler rotation.** Working it out re-runs the
  caller's `configure` delegate, and `IHttpClientFactory` rebuilds the handler chain roughly every
  two minutes, so the delegate ran for the life of the application and took any side effect in it
  along. Resolved once now
- **Hedging spans could end with no `outcome` tag.** The two early returns taken when an attempt
  succeeds before the launch loop finishes skipped `SetOutcome`, so a successful hedged call
  produced a span that was present in the trace but silent about what happened
- **A reloadable pipeline rebuilt on changes to unrelated named options.**
  `IOptionsMonitor.OnChange` fires for every named instance of the options type, and the
  subscription ignored which one changed — so a pipeline bound to `"billing"` rebuilt whenever
  `"api"` changed
- `RegistryDispose_DisposesReloadSubscriptions` asserted nothing about disposal and passed with
  the feature removed. It now uses a monitor that records whether its subscription was disposed

- **Timeout cancellation classification is now race-free.** `WasCancelledByTimeout` read
  `linkedCts.IsCancellationRequested && !userToken.IsCancellationRequested` as two separate reads,
  so a user cancellation landing between them was reported as `TimeoutRejectedException` instead of
  `OperationCanceledException`. The strategy now records *which* cause cancelled its linked token
  first via a compare-and-swap at the moment cancellation happens, so classification is a single
  read. Callers branch on that distinction to decide whether to retry, so the old behaviour could
  turn a deliberate shutdown into a retry storm. Costs 32 B per timeout plus a token registration
  when the incoming token is cancellable — see [docs/tradeoffs.md](docs/tradeoffs.md)
- **A typed strategy reached by an execution of a different result type now throws** instead of
  silently skipping itself. A skipped strategy is a retry that never retries or a circuit breaker
  that never breaks, on an execution that looks successful. The previous `Debug.WriteLine` warning
  was `[Conditional("DEBUG")]`, so it was compiled out of the shipped Release assembly and no
  consumer ever saw it. No public API can currently produce a mismatch; the guard exists so a
  future composition surface cannot reintroduce the silent skip
- **`resilion.hedging.attempts` is now incremented on the synchronous path.** It previously reported
  zero for sequential sync hedging even though the attempts ran and `OnHedging` fired, so the
  counter and the event handler disagreed
- **Strategies nested inside a hedging strategy now carry the enclosing pipeline's name.** Hedging
  copied `OperationKey`, `ContinueOnCapturedContext` and `Properties` onto its per-attempt contexts
  but not `PipelineName`, so every nested circuit breaker and attempt timeout emitted
  `pipeline.name = null`
- **Typed strategies now emit `ActivitySource` spans.** `RetryStrategy<T>`,
  `CircuitBreakerTypedStrategy<T>` and hedging's synchronous path emitted none, so any pipeline
  built with `Pipeline.Create<TResult>` produced no retry or circuit-breaker spans at all while the
  docs promised one per strategy execution

### Added

- `SECURITY.md` — supported versions, private vulnerability reporting, response targets and scope
- SourceLink, symbol packages (`.snupkg`) and deterministic release builds, so consumers can step
  into Resilion's source while debugging. Verified end to end: repository metadata with the commit
  SHA in the nuspec, a source map in the PDB, and the PDB present in the symbol package
- `docs/verification.md` — the command behind every factual claim the docs make about current
  behaviour, so no write-up has to be trusted from memory
- CONTRIBUTING.md § "Future-plans hygiene" — an item leaves `future-plans.md` in the same PR that
  implements it, claims about current behaviour must be re-verified, and an issue lives in exactly
  one of `future-plans.md` or `tradeoffs.md`

### Changed

- **Public API tracking now actually works.** The `PublicAPI.*.txt` files were hand-written prose —
  `namespace X;` grouping lines, unqualified member names, `#` comments the analyzer treats as API
  declarations — so RS0016 fired for essentially the whole surface and RS0017 for most declared
  lines (742 and 508 warnings respectively). They are regenerated in the analyzer's canonical
  format, and `ci.yml` builds with `-p:ContinuousIntegrationBuild=true`, which promotes RS0016 and
  RS0017 to errors. The gate is verified to fail: a stray public member errors, and removing a
  declared line errors. The previous claim that "CI builds will fail if public surface changes" was
  not true as configured
- Span tag emission consolidated into an internal `StrategyActivity` helper, so the tag set is
  written once and every strategy entry point — typed and non-generic, async and sync — emits an
  identical shape
- `release-nuget.yml` packs from a project array with a derived package count, instead of three
  hardcoded `dotnet pack` lines plus a hardcoded `-ne 3` assertion that would have broken every
  release the moment a fourth package landed. Adds an explicit symbol-package check before pushing
- `src/Directory.Build.props` uses `VersionPrefix` (1.0.0) rather than `Version` (0.1.0): setting
  `Version` explicitly suppresses `VersionSuffix` handling, so `-p:VersionSuffix=pre` was silently
  ignored
- README's synchronous-execution claim is scoped to what the code does. Every built-in strategy has
  a real synchronous implementation, but async fallback factories and async event handlers still
  thread-hop under a `SynchronizationContext`, sync hedging is sequential-only, sync retry delays
  are not `TimeProvider`-driven, and custom strategies must override `Execute`. That claim is why
  some users choose this library, so it states the exceptions
- `docs/telemetry.md` documents the four span tags the code actually sets (`strategy.name`,
  `pipeline.name`, `operation.key`, `outcome`) and the `outcome` vocabulary. It previously listed
  `strategy`, `attempt` and `duration_ms`, none of which exist
- CONTRIBUTING.md documents the Microsoft.Testing.Platform test filter syntax. The documented
  `dotnet test --filter "FullyQualifiedName~..."` is VSTest syntax that MTP **silently ignores**
  while running the entire suite
- Benchmark figures in README and `benchmarks/results/` re-measured after the timeout fix


## [1.0.0-pre] - 2026-09-02

### Added

- Initial project structure and build configuration
- Core abstractions: `Outcome<T>`, `ResilienceContext`, `Pipeline`, `Strategy`
- `IPipelineProvider<TKey>` — a read-only view over `ResiliencePipelineRegistry<TKey>` for consumers that only retrieve pipelines, never register them; registered in DI alongside the registry
- `BreakDurationGenerator` on both circuit breaker options classes — computes the break duration dynamically per trip (e.g. exponential backoff on repeated trips), via the new `BreakDurationGeneratorArgs`
- `MaxDelay` on both retry options classes — a global safety cap applied after `Delay` computes its value, for every backoff type including `RetryDelay.Custom`
- Configurable cap on `ResilienceContextPool` via `new ResilienceContextPool(maxPoolSize)` (defaults to 256, matching the previous hardcoded value)
- `docs/migration-from-polly.md` — concept mapping and before/after code samples for the five most common Polly → Resilion migrations
- `docs/comparison-with-polly.md` — honest side-by-side of where each library is stronger today, with a roadmap table to close the gap
- Extensive new test coverage: typed circuit breaker (previously 1 test), `SlidingWindow` direct tests, hedging latency-mode and cleanup-timeout tests, telemetry instrument verification, multi-strategy composition/integration tests, DI round-trip tests, ordering-validation error/warning split, `TypedStrategyComponent` mismatch, and `await using` disposal tests
- New benchmarks: circuit breaker under mixed Closed-state traffic, `ResilienceContextPool` Rent/Return vs allocation, and a 100k-execution GC pressure comparison against Polly — see [benchmarks/results](benchmarks/results/README.md) for numbers
- `ThrowOnOrderingErrors` on `PipelineBuilderBase` (default `true`) — dangerous strategy misorderings (CircuitBreaker outside Retry, Fallback not outermost) now throw `InvalidOperationException` at `Build()` time instead of only warning via `Debug.WriteLine`; situational issues (3+ Timeouts, Hedging+Retry) remain advisory-only regardless
- `IAsyncDisposable` on `Pipeline`, `Pipeline<TResult>`, `Strategy`, `Strategy<TResult>`, and the internal `PipelineComponent` chain — `await using` now works; the default `DisposeAsync()` falls back to the existing sync `Dispose()` so custom strategies need no changes
- `test.yml` `aot-verify` job — publishes the samples project with Native AOT on every PR/push and runs the resulting binary, so the `IsAotCompatible=true` claim stays continuously verified

### Changed

- `AddResilion()` renamed to `AddResilienceServices()` — the old name follows .NET naming conventions poorly (describes the library, not what it adds); `AddResilion()` remains as an `[Obsolete]` alias delegating to the new name
- `global.json` SDK floor bumped from the stale `8.0.100` to `8.0.404`; `rollForward: latestMajor` unchanged (intentional — CI matrix jobs each install a single SDK major and rely on it to satisfy the floor)
- CI (`test.yml`) now runs the test suite under both `8.0.x` and `9.0.x` SDKs via a matrix, instead of `9.0.x` only
- `CircuitBreakerStrategy` and `CircuitBreakerTypedStrategy<T>` now share a single `CircuitBreakerStateMachine` instead of ~200 duplicated lines each — the two variants can no longer diverge in behavior
- `PipelineBuilder` and `PipelineBuilder<TResult>` now share `PipelineBuilderBase` for their common properties and `EmitWarnings`
- The default "handle everything except cancellation" predicate is now a single `OutcomePredicates.DefaultShouldHandle<T>()`, shared by all four options classes that used to copy-paste it
- `ResilienceEventHandler<TArgs>.Invoke()` skips the `Task.Run` thread-hop entirely when no `SynchronizationContext` is present (the common case); only pays the two-thread cost when one exists (WPF/WinForms/legacy ASP.NET)
- Samples project restructured: the original 6 samples stay in `Program.cs`; 6 new ones (DI, typed HTTP-status retry, rate limiter, hedging `ActionGenerator`, `BreakDurationGenerator`, state-parameter) live as one file each under `Samples/`
- `TypedStrategyComponent` now emits a `Debug.WriteLine` warning when it skips a strategy due to a result-type mismatch, instead of skipping silently
- README's "Why Resilion?" tagline no longer gates on "experienced" developers; added a "Free forever" banner, corrected the DI/telemetry code snippets, and added a real benchmark-numbers summary

### Fixed

- **Race condition** in `CircuitBreakerTypedStrategy<T>`: recording an outcome and reading the failure ratio were two separate lock acquisitions, letting a concurrent caller observe a stale or out-of-range ratio between them. Now both circuit breaker variants share `SlidingWindow.RecordAndGetRatio`, which combines the two under one lock.
- `Strategy`/`Strategy<TResult>`'s default `Execute()` (used only by custom strategies that don't override it) now throws `InvalidOperationException` when a `SynchronizationContext` is present, instead of silently risking deadlock
- Negative-`TimeSpan` validation on `RetryDelay.Constant`/`Linear`/`Exponential`
- `ResiliencePipelineRegistry<TKey>.GetPipeline()` no longer caches a faulted `Lazy<Pipeline>` after a failed lookup — registering the key afterward and retrying now succeeds instead of replaying the cached failure
- `PipelineBuilder`/`PipelineBuilder<TResult>` now throw `InvalidOperationException` if used after `Build()` has already been called, instead of silently accumulating strategies for a pipeline that was already constructed
- Hedging's sync `Execute()` path now throws `InvalidOperationException` for parallel/latency `HedgingDelay` settings instead of silently degrading to sequential execution with no indication hedging wasn't actually hedging
- `RealWorldScenarioBenchmarks`' DB-query pipeline had Fallback innermost (`Timeout → Retry → Fallback`), so it intercepted every failure before Retry ever saw one, making the retry a no-op — caught by the new `ThrowOnOrderingErrors` default while re-verifying the benchmark suite; reordered to `Fallback → Timeout → Retry`

### Removed

- Dead telemetry instruments `resilion.strategy.executions` and `resilion.strategy.duration` — declared but never incremented by any strategy
- `HedgingRejectedException` — defined but never constructed or thrown anywhere

### Documentation

- Corrected `SlidingWindow`'s XML remarks and `docs/architecture.md`'s thread-safety section, both of which claimed `Interlocked` counters where the code has always used a single lock
- `docs/tradeoffs.md`'s `ResilienceContextPool` section updated for the renamed/configurable pool cap field
- Added `BreakDurationGenerator` / `MaxDelay` rows to the circuit breaker and retry options reference tables
- Added an XML doc `<remarks>` on `Outcome<T>`'s implicit operator warning about the `Outcome<Exception>` success-vs-failure ambiguity
- Documented ordering validation (`ThrowOnOrderingErrors`, `SuppressOrderingWarnings`) in `docs/pipelines.md`

[Unreleased]: https://github.com/FreakyAli/Resilion/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/FreakyAli/Resilion/compare/v1.0.0-pre...v1.0.0
[1.0.0-pre]: https://github.com/FreakyAli/Resilion/releases/tag/v1.0.0-pre
