# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

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

[Unreleased]: https://github.com/FreakyAli/Resilion/compare/v1.0.0-pre...HEAD
[1.0.0-pre]: https://github.com/FreakyAli/Resilion/releases/tag/v1.0.0-pre
