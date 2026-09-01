# Hedging

Reduces tail latency by racing concurrent attempts. When the primary request is slow, a secondary fires. Unhandled outcomes (success) complete the operation immediately. Handled outcomes (failures) trigger further attempts, and if all are handled, the last handled outcome is returned.

## Basic usage

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddHedging(
    new HedgingStrategyOptions<string>
    {
        MaxHedgedAttempts = 3,
        HedgingDelay = TimeSpan.FromSeconds(2),
    }));

var result = await pipeline.ExecuteAsync(async ct =>
    await httpClient.GetStringAsync("https://api.example.com", ct));
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxHedgedAttempts` | `int` | 2 | Total attempts including primary |
| `HedgingDelay` | `TimeSpan` | 2s | Wait before launching next attempt |
| `ShouldHandle` | `Func<Outcome<T>, bool>` | All except `OperationCanceledException` | What triggers hedging |
| `ActionGenerator` | `Func<HedgingActionContext, Func<CancellationToken, ValueTask<T>>?>` | null | Custom action per attempt |
| `OnHedging` | `ResilienceEventHandler<OnHedgingEvent<T>>` | null | Fired before each hedged launch |

## Three modes

### Latency mode (delay > 0) — default

Primary runs. If it hasn't completed after `HedgingDelay`, secondary fires. Both race. First success wins.

```csharp
HedgingDelay = TimeSpan.FromSeconds(2)  // Wait 2s, then hedge
```

Best for: reducing P99 latency when most requests are fast.

### Parallel mode (delay = 0)

All attempts fire simultaneously. Maximum parallelism, maximum resource usage.

```csharp
HedgingDelay = TimeSpan.Zero  // Fire all immediately
```

Best for: latency-critical paths where cost of extra requests is acceptable.

### Sequential mode (InfiniteTimeSpan)

Each attempt waits for the previous to fail before starting. No parallelism.

```csharp
HedgingDelay = Timeout.InfiniteTimeSpan  // Wait for failure, then try next
```

Best for: fallback to alternative endpoints without parallel overhead.

## Custom actions per attempt

Route different attempts to different endpoints:

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddHedging(
    new HedgingStrategyOptions<string>
    {
        MaxHedgedAttempts = 3,
        HedgingDelay = Timeout.InfiniteTimeSpan,
        ActionGenerator = ctx => ctx.AttemptNumber switch
        {
            0 => ct => CallPrimaryAsync(ct),
            1 => ct => CallSecondaryAsync(ct),
            2 => ct => CallCacheAsync(ct),
            _ => null,  // null = skip this attempt
        },
    }));
```

Returning `null` from `ActionGenerator` skips that attempt index.

## Cancellation

- Each attempt gets its own linked `CancellationTokenSource`
- When one attempt wins, all others are cancelled
- **All cancelled tasks are awaited** before returning — prevents resource leaks (connections, streams)
- If user's original token cancels, all attempts cancel and `OperationCanceledException` propagates

This is a key difference from Polly, which cancels but does not await losing tasks.

## All attempts fail

When every attempt fails, the last failure's exception propagates. Only the most recent exception surfaces — earlier attempt exceptions are not aggregated. (See [future-plans.md](future-plans.md#14-hedging-hedgingrejectedexception-with-aggregated-errors) for planned improvement.)

## Typed pipelines only

Hedging requires `Pipeline<TResult>` because it must produce a result of the correct type.

## Sync execution

Sync path always runs sequentially regardless of `HedgingDelay` — parallel execution requires async. Each attempt runs to completion (or failure) before the next starts.

## Result-based hedging

Hedge on specific result values, not just exceptions:

```csharp
var pipeline = Pipeline.Create<int>(b => b.AddHedging(
    new HedgingStrategyOptions<int>
    {
        ShouldHandle = outcome =>
            outcome.TryGetResult(out var val) && val < 0,  // Negative = retry
    }));
```

## Telemetry

Emits `resilion.hedging.attempts` counter for each hedged attempt launched (not the primary). Subscribe with `.AddMeter("Resilion")`.
