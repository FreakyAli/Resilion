# Circuit Breaker

Monitors failure rates and stops calling a failing dependency when the failure ratio exceeds a threshold. Protects your system from cascading failures and gives the downstream service time to recover.

## Basic usage

```csharp
var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatioThreshold = 0.5,
    MinimumThroughput = 10,
    SamplingDuration = TimeSpan.FromSeconds(30),
    BreakDuration = TimeSpan.FromSeconds(30),
}));

try
{
    await pipeline.ExecuteAsync(async ct =>
        await httpClient.GetStringAsync("https://fragile-service.example.com", ct));
}
catch (CircuitBrokenException ex)
{
    Console.WriteLine($"Circuit is {ex.CircuitState}, retry after {ex.RetryAfter.TotalSeconds:F0}s");
}
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `FailureRatioThreshold` | `double` | 0.5 (50%) | Failure ratio that trips the circuit |
| `SamplingDuration` | `TimeSpan` | 30s | Sliding window for tracking |
| `MinimumThroughput` | `int` | 10 | Min calls before ratio is evaluated |
| `BreakDuration` | `TimeSpan` | 30s | How long circuit stays open |
| `ShouldHandle` | `Func<Exception, bool>` | All except `OperationCanceledException` | What counts as failure |
| `OnOpened` | `ResilienceEventHandler` | null | Fired on Closed → Open |
| `OnClosed` | `ResilienceEventHandler` | null | Fired on HalfOpen → Closed |
| `OnHalfOpened` | `ResilienceEventHandler` | null | Fired on Open → HalfOpen |
| `ManualControl` | `CircuitBreakerManualControl` | null | Manual isolate/reset |

## State machine

```
     ┌─────────┐  failure ratio exceeded   ┌──────┐
     │ Closed  │ ────────────────────────▸  │ Open │
     │ (normal)│                            │(reject)
     └────▲────┘                            └──┬───┘
          │                                    │
          │  probe succeeds         break duration expires
          │                                    │
     ┌────┴─────┐                              │
     │ HalfOpen │ ◂────────────────────────────┘
     │ (probe)  │
     └──────────┘
          │
          │ probe fails
          │
          └──────────────────────────────▸ Open
```

**Closed** — normal operation. Successes and failures tracked in sliding window.

**Open** — all calls rejected immediately with `CircuitBrokenException`. No execution attempted. After `BreakDuration`, transitions to HalfOpen.

**HalfOpen** — one probe call allowed through. If it succeeds, circuit closes. If it fails, circuit reopens.

## Sliding window

Failures are tracked using a bucketed sliding window (10 time buckets). This gives O(1) memory regardless of throughput. Old buckets expire automatically.

The circuit trips when BOTH conditions are met:
1. Failure ratio ≥ `FailureRatioThreshold`
2. Total calls in window ≥ `MinimumThroughput`

`MinimumThroughput` prevents false trips during low traffic (e.g., 1 failure out of 2 calls = 50%, but not meaningful).

## Exception counting

- Exceptions matching `ShouldHandle` are counted as failures AND rethrown
- Exceptions NOT matching `ShouldHandle` are counted as successes and rethrown
- `OperationCanceledException` is never counted as a failure (user-initiated, not a dependency failure)

## Manual control

```csharp
var control = new CircuitBreakerManualControl();

var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    ManualControl = control,
}));

// Force open (e.g., during maintenance)
await control.IsolateAsync();

// Force closed (e.g., after deployment)
await control.ResetAsync();
```

Each `CircuitBreakerManualControl` instance can only be bound to one circuit breaker. Reusing across multiple breakers throws `InvalidOperationException`.

## Result-based circuit breaker

On typed pipelines, specific result values count as failures:

```csharp
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddCircuitBreaker(
    new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = outcome =>
            outcome.Exception is HttpRequestException
            || (outcome.TryGetResult(out var r) && (int)r.StatusCode >= 500),
    }));
```

## Thread safety

- State reads (Closed check) use volatile — no lock on hot path
- State transitions acquire `System.Threading.Lock` — only one thread transitions
- Sliding window has its own internal lock for bucket rotation
- Event callbacks fire outside the lock to prevent deadlock

## Composing with Retry

Place circuit breaker inside retry so each attempt is tracked individually:

```csharp
var pipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = ex => ex is CircuitBrokenException or HttpRequestException,
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions()));
```

## Telemetry

Emits `resilion.circuit_breaker.state_changes` counter on every state transition. Subscribe with `.AddMeter("Resilion")`.
