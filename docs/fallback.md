# Fallback

Provides a substitute result when the primary operation fails. The simplest form of graceful degradation.

## Basic usage

```csharp
// Constant value — simplest case
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackAction = "default-response",
    }));

var result = await pipeline.ExecuteAsync(ct =>
{
    throw new HttpRequestException("Service down");
    return new ValueTask<string>("unreachable");
});
// result == "default-response"
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `FallbackAction` | `FallbackAction<T>` | **required** | Value, sync factory, or async factory |
| `ShouldHandle` | `Func<Outcome<T>, bool>` | All except `OperationCanceledException` | What triggers fallback |
| `OnFallback` | `ResilienceEventHandler<OnFallbackEvent<T>>` | null | Fired when fallback activates |

## FallbackAction — three forms

`FallbackAction<T>` supports implicit conversion from three types, so you write natural C#:

```csharp
// 1. Constant value
FallbackAction = "cached-default"

// 2. Sync factory — compute from the failure
Func<FallbackContext<string>, string> factory =
    ctx => $"Error: {ctx.Exception?.Message}";
FallbackAction = factory

// 3. Async factory — call a secondary service or cache
Func<FallbackContext<string>, ValueTask<string>> asyncFactory =
    async ctx => await cache.GetAsync("fallback-key");
FallbackAction = asyncFactory
```

The factory receives `FallbackContext<T>` which includes:
- `Outcome` — the failed outcome (exception or bad result)
- `Exception` — shortcut to `Outcome.Exception`
- `Context` — the `ResilienceContext` with cancellation token and properties

## Result-based fallback

Fallback can trigger on specific result values, not just exceptions:

```csharp
var pipeline = Pipeline.Create<int>(b => b.AddFallback(
    new FallbackStrategyOptions<int>
    {
        FallbackAction = 0,
        ShouldHandle = outcome =>
            outcome.TryGetResult(out var val) && val < 0,  // Negative = failure
    }));
```

## Typed pipelines only

Fallback requires `Pipeline<TResult>` because it must produce a substitute value of the correct type. You cannot add fallback to a non-generic `Pipeline`.

## Exception swallowing

When fallback activates, the original exception is swallowed. The fallback result is returned as if the operation succeeded. If you need to log the original error, use `OnFallback`:

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackAction = "cached-value",
        OnFallback = e =>
            logger.LogWarning(e.Outcome.Exception, "Fallback activated, returning {Result}", e.FallbackResult),
    }));
```

## Fallback factory that throws

If `FallbackAction` itself throws, that exception propagates to the caller. The fallback strategy does not retry its own fallback.

## Composing with Retry

Place fallback outside retry to catch exhausted retries:

```csharp
var pipeline = Pipeline.Create<string>(b => b
    .AddFallback(new FallbackStrategyOptions<string>
    {
        FallbackAction = "all-retries-failed-fallback",
    })
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.None,
    }));
// Retries 3 times. If all fail, fallback returns the substitute.
```

## Telemetry

Emits `resilion.fallback.activations` counter when fallback triggers. Subscribe with `.AddMeter("Resilion")`.
