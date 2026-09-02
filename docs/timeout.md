# Timeout

Enforces a time limit on operations using cooperative cancellation.

## Basic usage

```csharp
var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));

try
{
    await pipeline.ExecuteAsync(async ct =>
    {
        return await httpClient.GetStringAsync("https://slow-api.example.com", ct);
    });
}
catch (TimeoutRejectedException ex)
{
    Console.WriteLine($"Timed out after {ex.ElapsedTime.TotalSeconds:F1}s");
}
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Timeout` | `TimeSpan` | 30 seconds | Duration before timeout fires |
| `TimeoutGenerator` | `Func<TimeoutGeneratorArgs, TimeSpan>` | null | Dynamic timeout per execution |
| `OnTimeout` | `ResilienceEventHandler<OnTimeoutArgs>` | null | Callback when timeout occurs |

### Special values

- `TimeSpan.Zero` — immediately times out (useful for testing)
- `Timeout.InfiniteTimeSpan` — disables timeout (passthrough, no CTS allocated)

## How it works

1. Creates a linked `CancellationTokenSource` combining the user's token with a timer
2. Passes the linked token to the inner delegate via `ResilienceContext.CancellationToken`
3. If timer fires before delegate completes, linked token cancels
4. Catches `OperationCanceledException` from the delegate and wraps it in `TimeoutRejectedException`
5. If user's original token was cancelled (not the timer), `OperationCanceledException` propagates unchanged

**Cooperative only.** The delegate MUST observe the `CancellationToken`. If it ignores the token, timeout has no effect — Resilion cannot forcibly abort operations. This is a deliberate design choice. Pessimistic timeout (background thread abandonment) leaks resources and creates unpredictable state.

## User cancellation vs timeout

Resilion distinguishes between the two:

| Cause | Exception thrown | How to catch |
|-------|-----------------|-------------|
| Timeout expired | `TimeoutRejectedException` | `catch (TimeoutRejectedException)` |
| User cancelled | `OperationCanceledException` | `catch (OperationCanceledException)` |

`TimeoutRejectedException` includes:
- `ConfiguredTimeout` — the duration that was set
- `ElapsedTime` — actual time elapsed before timeout
- `InnerException` — the original `OperationCanceledException`

## Per-attempt vs total timeout

Strategy ordering determines which timeout applies:

```csharp
// Total timeout (outer) + per-attempt timeout (inner)
var pipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))    // Caps entire pipeline including retries
    .AddRetry(...)
    .AddTimeout(TimeSpan.FromSeconds(5)));   // Each attempt gets 5s
```

## Dynamic timeout

```csharp
var pipeline = Pipeline.Create(b => b.AddTimeout(new TimeoutStrategyOptions
{
    TimeoutGenerator = args =>
    {
        // Different timeout based on operation
        return args.Context.OperationKey switch
        {
            "fast-op" => TimeSpan.FromSeconds(2),
            "slow-op" => TimeSpan.FromSeconds(60),
            _ => TimeSpan.FromSeconds(10),
        };
    },
}));
```

## Telemetry

Emits `resilion.timeout.expirations` counter when timeout fires. Subscribe with `.AddMeter("Resilion")`.
