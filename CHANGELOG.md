# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- CI `aot-verify` job — publishes the samples project with Native AOT on every PR/push and runs the resulting binary, so the `IsAotCompatible=true` claim stays continuously verified

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
