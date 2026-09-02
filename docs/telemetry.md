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

| Tag | Example | Notes |
|-----|---------|-------|
| `pipeline.name` | `"http-api"` | Name passed to `AddResiliencePipeline()` or pipeline name in registry |
| `operation.key` | `"GET /users"` | Custom operation key from `ResilienceContext` |
| `strategy` | `"retry"` | Strategy type (implicit in metric name) |

Example: A `resilion.retry.attempts` counter for a named pipeline emits as:
```
resilion.retry.attempts{pipeline.name="http-api", operation.key="GET /users"} = 3
```

This allows dashboards and alerting rules to group by pipeline and operation.

## ActivitySource spans

Resilion also emits OpenTelemetry `Activity` spans for distributed tracing. Each strategy execution creates a span named after the strategy.

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

Each strategy creates a span with contextual information:

```
Activity.OperationName = "Retry"  // or Timeout, CircuitBreaker, etc.
Activity.Tags:
  - "strategy" → "retry"
  - "pipeline.name" → "http-api"
  - "operation.key" → "GET /users"
  - "attempt" → "1" (for retryable strategies)
  - "duration_ms" → "150" (execution time)
```

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
