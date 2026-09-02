# Retry

Re-executes a failed operation up to a configured number of times, with configurable delays between attempts.

## Basic usage

```csharp
var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
    UseJitter = true,
}));

var result = await pipeline.ExecuteAsync(async ct =>
{
    var response = await httpClient.GetAsync("https://api.example.com", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(ct);
});
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRetryAttempts` | `int` | 3 | Number of retries (not counting initial call). 0 = no retries. |
| `Delay` | `RetryDelay` | Exponential(1s, max 30s) | Delay strategy between retries |
| `UseJitter` | `bool` | `true` | Apply decorrelated jitter to delays |
| `MaxDelay` | `TimeSpan?` | null | Global safety cap applied after `Delay` computes its value — useful with `RetryDelay.Custom` |
| `ShouldHandle` | `Func<Exception, bool>` | All except `OperationCanceledException` | Which exceptions trigger retry |
| `OnRetry` | `ResilienceEventHandler<RetryAttemptEvent>` | null | Callback before each retry wait |

## Delay strategies

Resilion uses `RetryDelay` — a discriminated union that makes delay configuration mutually exclusive by construction. No silent overrides.

```csharp
// Constant: same delay every time
Delay = RetryDelay.Constant(TimeSpan.FromMilliseconds(500))
// Produces: 500ms, 500ms, 500ms, ...

// Linear: baseDelay × attemptNumber
Delay = RetryDelay.Linear(TimeSpan.FromSeconds(1))
// Produces: 1s, 2s, 3s, ...

// Exponential: baseDelay × 2^(attemptNumber-1)
Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1))
// Produces: 1s, 2s, 4s, 8s, ... (capped at maxDelay, default 30s)

// Custom: any function
Delay = RetryDelay.Custom(ctx => TimeSpan.FromSeconds(ctx.AttemptNumber * 5))

// None: immediate retry (useful for testing)
Delay = RetryDelay.None
```

### Jitter

When `UseJitter = true` (default), ±25% randomness is applied to computed delays. This prevents thundering herd problems where many clients retry at the same instant after a shared failure.

Jitter is recommended for all production use. Disable only for deterministic testing.

## Result-based retry (typed pipelines)

On typed pipelines, retry can inspect return values — not just exceptions:

```csharp
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddRetry(
    new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
        ShouldHandle = outcome =>
            outcome.Exception is HttpRequestException
            || (outcome.TryGetResult(out var r) && (int)r.StatusCode >= 500),
    }));
```

`ShouldHandle` receives `Outcome<T>` which wraps either the result or exception. Use `TryGetResult` to inspect results without throwing.

## Exhaustion behavior

When all retries are exhausted:
- **Exception failures**: last attempt's exception propagates with original stack trace. No wrapping in a `RetryExhaustedException` — your `catch (HttpRequestException)` still works.
- **Result failures**: last attempt's result is returned as-is.

## Cancellation

- `OperationCanceledException` is never retried by default
- Cancellation is checked before each attempt and during delay waits
- If token is cancelled during delay, retry stops immediately with `OperationCanceledException`

## Composing with Timeout

```csharp
// Total timeout (outer) + per-attempt timeout (inner)
var pipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))    // Total budget
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
        ShouldHandle = ex => ex is TimeoutRejectedException,
    })
    .AddTimeout(TimeSpan.FromSeconds(5)));   // Per attempt
```

## OnRetry callback

```csharp
Action<RetryAttemptEvent> onRetry = e =>
    logger.LogWarning("Retry #{Attempt}, waiting {Delay}ms. Error: {Error}",
        e.AttemptNumber, e.RetryDelay.TotalMilliseconds, e.Exception.Message);

var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    OnRetry = onRetry,
}));
```

## Telemetry

Emits `resilion.retry.attempts` counter on each retry attempt. Subscribe with `.AddMeter("Resilion")`.
