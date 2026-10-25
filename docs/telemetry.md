# Telemetry

Resilion emits metrics via .NET's built-in `System.Diagnostics.Metrics`. Zero overhead when no listener is attached.

## Metrics

All metrics are on the `"Resilion"` meter.

| Metric | Type | Emitted when |
|--------|------|-------------|
| `resilion.retry.attempts` | Counter | Each retry attempt |
| `resilion.timeout.expirations` | Counter | Timeout fires |
| `resilion.circuit_breaker.state_changes` | Counter | Any state transition |
| `resilion.fallback.activations` | Counter | Fallback triggers |
| `resilion.hedging.attempts` | Counter | Hedged attempt launches |
| `resilion.rate_limiter.rejections` | Counter | Rate limit rejection |

### Metric tags

All counters are tagged with context about the operation:

Counters carry exactly two tags. Which strategy emitted the measurement is identified by the
instrument name itself (`resilion.retry.attempts`, `resilion.timeout.expirations`, …), not by a tag.

| Tag | Example | Notes |
|-----|---------|-------|
| `pipeline.name` | `"http-api"` | Name passed to `AddResiliencePipeline()` or the registry key. **Null** for unnamed pipelines, and null for any strategy nested inside hedging — see the gap note below |
| `operation.key` | `"GET /users"` | Custom operation key from `ResilienceContext`. Null unless you set it |

Example: A `resilion.retry.attempts` counter for a named pipeline emits as:
```
resilion.retry.attempts{pipeline.name="http-api", operation.key="GET /users"} = 3
```

This allows dashboards and alerting rules to group by pipeline and operation.

Counters are emitted identically on the asynchronous and synchronous paths, and strategies nested
inside a hedging strategy carry the enclosing pipeline's name. Both are pinned by tests
(`HedgingAttempts_SyncAndAsyncAgreeOnCount`, `NestedStrategyInsideHedging_EmitsPipelineNameTag`),
because both were previously broken in ways that were invisible until someone read a dashboard.

## ActivitySource spans

Resilion also emits OpenTelemetry `Activity` spans for distributed tracing, named after the strategy.

### Subscribing to ActivitySource

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Resilion"));
```

Or configure a global listener:

```csharp
var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "Resilion",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
};
ActivitySource.AddActivityListener(listener);
```

### Span names and tags

A span carries exactly four tags. These are the only keys the code sets — there is no `attempt`
tag and no `duration_ms` tag (use the span's own start/stop timestamps for duration).

```
Activity.OperationName = "Retry"  // or Timeout, Fallback, CircuitBreaker, RateLimiter, Hedging
Activity.Tags:
  - "strategy.name"  → "Retry"
  - "pipeline.name"  → "http-api"      // null for unnamed pipelines
  - "operation.key"  → "GET /users"    // null unless set on ResilienceContext
  - "outcome"        → "retry_exhausted"
```

`outcome` is set when the strategy finishes, and its vocabulary is strategy-specific:

| Value | Emitted by |
|-------|------------|
| `success` | every strategy, when the wrapped call succeeded |
| `failure` | every strategy, when the wrapped call returned a handled failure |
| `exception` | circuit breaker, when the call threw |
| `timeout` / `no_timeout` | timeout |
| `rejected` | circuit breaker (open circuit), rate limiter |
| `retry_exhausted` | retry, when all attempts were used |
| `fallback_applied` | fallback, when the fallback value was returned |
| `hedging_exhausted` | hedging, when no attempt succeeded |

## Subscribing

### dotnet-counters (CLI)

```bash
dotnet counters monitor --name <process-name> --counters Resilion
```

Or monitor by process ID:

```bash
dotnet counters monitor --process-id <pid> --counters Resilion
```

### OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Resilion"));
```

### Manual MeterListener

```csharp
var listener = new MeterListener();
listener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "Resilion")
        listener.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
    Console.WriteLine($"{instrument.Name}: {value}"));
listener.Start();
```

## Zero-cost when unused

After initialization, `Counter<long>.Add(1)` is a no-op at the runtime level when no `MeterListener` is subscribed. No allocation, no work during the metric recording path. Note: `ResilionTelemetry` eagerly allocates the `Meter`, `ActivitySource`, and measurement instruments at startup. Metrics only become active when something listens.

## Strategy callbacks vs metrics

Two complementary systems:

- **Callbacks** (`OnRetry`, `OnTimeout`, etc.) — inline, per-pipeline, for custom logic (logging, alerting, request mutation)
- **Metrics** — global, aggregated, for observability dashboards and alerting systems

Both fire on the same events. Use callbacks for per-request decisions. Use metrics for aggregate monitoring.
