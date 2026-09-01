# Rate Limiting

Throttles executions using .NET's built-in `System.Threading.RateLimiting`. Lives in the `Resilion.RateLimiting` package.

## Basic usage

```csharp
using Resilion;
using Resilion.RateLimiting;
using System.Threading.RateLimiting;

var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 10,
    QueueLimit = 0,
});

var pipeline = Pipeline.Create(b => b.AddRateLimiter(
    new RateLimiterStrategyOptions { RateLimiter = limiter }));

try
{
    await pipeline.ExecuteAsync(async ct => await CallApiAsync(ct));
}
catch (RateLimitRejectedException ex)
{
    Console.WriteLine($"Rate limited. Retry after: {ex.RetryAfter?.TotalSeconds:F1}s");
}
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RateLimiter` | `RateLimiter` | **required** | .NET rate limiter instance |
| `OnRejected` | `ResilienceEventHandler<OnRateLimitRejectedEvent>` | null | Fired on rejection |

## Supported rate limiter types

All .NET built-in algorithms work:

```csharp
// Concurrency — max simultaneous executions
new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 10 })

// Token bucket — tokens replenish over time
new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 100,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    TokensPerPeriod = 10,
})

// Fixed window — permits per time window
new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
{
    PermitLimit = 100,
    Window = TimeSpan.FromMinutes(1),
})

// Sliding window — smoother distribution
new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
{
    PermitLimit = 100,
    Window = TimeSpan.FromMinutes(1),
    SegmentsPerWindow = 6,
})
```

## Lifetime management

The strategy does NOT own the `RateLimiter`. You (or your DI container) are responsible for disposing it. The strategy acquires a lease before execution, runs the delegate, and disposes the lease in `finally` — even on exceptions.

## RetryAfter

When the rate limiter provides `RetryAfter` metadata (e.g., `TokenBucketRateLimiter`), it's included in `RateLimitRejectedException.RetryAfter`. Combine with Retry to automatically wait:

```csharp
var pipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = ex => ex is RateLimitRejectedException,
        Delay = RetryDelay.Custom(ctx => TimeSpan.FromSeconds(1)), // or use RetryAfter
    })
    .AddRateLimiter(new RateLimiterStrategyOptions { RateLimiter = limiter }));
```

## Sync execution

The sync path uses `AttemptAcquire` (non-blocking). If the limiter supports queueing (`QueueLimit > 0`), the sync path cannot wait in the queue — it only attempts immediate acquisition.

## Telemetry

Emits `resilion.rate_limiter.rejections` counter on rejection. Subscribe with `.AddMeter("Resilion")`.
