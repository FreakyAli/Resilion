# Troubleshooting

## Timeout has no effect

**Problem**: Operation runs past the configured timeout without being cancelled.

**Cause**: Timeout relies on cooperative cancellation. The delegate must observe `CancellationToken`.

**Fix**: Pass the `ct` parameter to all async operations:

```csharp
// BAD — ignores cancellation
await pipeline.ExecuteAsync(async ct =>
    await SomeCall());  // token not passed

// GOOD
await pipeline.ExecuteAsync(async ct =>
    await SomeCall(ct));  // token forwarded
```

See [cancellation.md](cancellation.md) for details.

---

## Retry doesn't retry my exception

**Problem**: Exception thrown but not retried.

**Checklist**:
- `OperationCanceledException` is never retried by default
- Custom `ShouldHandle` predicate may not match your exception type
- `MaxRetryAttempts = 0` disables retries

**Fix**: Check your `ShouldHandle` predicate. Default handles all exceptions except `OperationCanceledException`:

```csharp
// Only retries HttpRequestException:
ShouldHandle = ex => ex is HttpRequestException

// Retries everything including OCE (unusual):
ShouldHandle = _ => true
```

---

## Circuit breaker never trips

**Problem**: Lots of failures but circuit stays Closed.

**Checklist**:
- `MinimumThroughput` not met? (default: 10 calls required before ratio evaluated)
- `FailureRatioThreshold` too high? (default: 0.5 = 50%)
- Exception not matching `ShouldHandle`? (`OperationCanceledException` doesn't count)
- `SamplingDuration` too short? (failures expire from the window)

---

## Circuit breaker stays open

**Problem**: Circuit tripped and never recovers.

**Cause**: `BreakDuration` hasn't elapsed, or probe calls in HalfOpen keep failing.

**Fix**: Check `BreakDuration` (default: 30s). After it expires, one probe call is allowed. If the probe fails, circuit reopens for another `BreakDuration`.

To force-close: use `CircuitBreakerManualControl.ResetAsync()`.

---

## "No pipeline registered with key" from DI

**Problem**: `KeyNotFoundException` when resolving from `ResiliencePipelineRegistry`.

**Cause**: Pipeline configurations registered via `AddResiliencePipeline` need to be applied to the registry before accessing it.

**Fix**: When you resolve `ResiliencePipelineRegistry<string>` from the service provider, all registered `IPipelineConfigurator` services are automatically applied:

```csharp
var services = new ServiceCollection();
services.AddResiliencePipeline("my-pipeline", b => b.AddRetry(...));
var sp = services.BuildServiceProvider();

// Registry is built and configured when resolved
var registry = sp.GetRequiredService<ResiliencePipelineRegistry<string>>();
var pipeline = registry.GetPipeline("my-pipeline");  // Works!
```

---

## Fallback not available on non-generic Pipeline

**Problem**: `AddFallback` method doesn't appear on `PipelineBuilder`.

**Cause**: Fallback requires `Pipeline<TResult>` because it produces a substitute value of a specific type.

**Fix**: Use `Pipeline.Create<T>(...)` instead of `Pipeline.Create(...)`:

```csharp
// Won't compile — no AddFallback on PipelineBuilder
Pipeline.Create(b => b.AddFallback(...));

// Works — typed pipeline
Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string> { FallbackAction = "default" }));
```

Same applies to `AddHedging`.

---

## My callback doesn't compile

**Problem**: `CS0029: Cannot implicitly convert type 'System.Action<T>' to 'ResilienceEventHandler<T>'`

**Cause**: you are on pre-1.0 code, or carrying a pre-1.0 example. `ResilienceEventHandler<TArgs>`
used to be a wrapper struct with implicit conversions from `Action<TArgs>`; since 1.0 it is a
delegate returning `ValueTask`.

**Fix**: drop the wrapper and return `default` from a synchronous handler.

```csharp
// Before 1.0 — the wrapper was mandatory, because a lambda could not convert to a struct (CS1660).
OnRetry = new Action<RetryAttemptEvent>(e => logger.Log(e.AttemptNumber))

// 1.0 onwards.
OnRetry = e => { logger.Log(e.AttemptNumber); return default; }
OnRetry = LogRetry;                             // method groups work too
OnRetry = async e => await TrackAsync(e);       // as does async
```

Returning `default` costs nothing: `ValueTask` is a struct, so a synchronous handler allocates
exactly as much as an `Action<T>` would, which is zero.

## Hedging sync path throws for parallel/latency mode

**Problem**: Calling `Execute` (sync) on a pipeline with `HedgingDelay = TimeSpan.Zero` (parallel mode) or any positive delay (latency mode) throws `InvalidOperationException`.

**Cause**: Parallel/latency hedging requires concurrent execution, which the sync path can't provide. Rather than silently running the attempts sequentially — defeating the point of hedging with no indication anything was wrong — `Execute()` throws.

**Fix**: Use `ExecuteAsync` for parallel or latency hedging, or set `HedgingDelay = Timeout.InfiniteTimeSpan` (sequential mode) if you specifically need the sync path.
