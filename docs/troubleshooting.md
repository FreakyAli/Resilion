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

## ResilienceEventHandler won't accept my lambda

**Problem**: `CS1660: Cannot convert lambda expression to type 'ResilienceEventHandler<T>?'`

**Cause**: C# can't chain two implicit conversions (lambda → `Action<T>` → `ResilienceEventHandler<T>`).

**Fix**: Assign to a typed variable first:

```csharp
// Won't compile directly in options initializer
OnRetry = (RetryAttemptEvent e) => logger.Log(e.AttemptNumber)  // CS1660

// Works — explicit Action<T>
Action<RetryAttemptEvent> onRetry = e => logger.Log(e.AttemptNumber);
// Then: OnRetry = onRetry
```

---

## Hedging sync path runs sequentially

**Problem**: Hedging with `HedgingDelay = TimeSpan.Zero` (parallel mode) doesn't run attempts in parallel when using `Execute` (sync).

**Cause**: Parallel execution requires async. The sync path always runs sequentially regardless of `HedgingDelay`.

**Fix**: Use `ExecuteAsync` for parallel hedging.
