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

## Subscribing

### dotnet-counters (CLI)

```bash
dotnet counters monitor --counters Resilion
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

`Counter<long>.Add(1)` is a no-op at the runtime level when no `MeterListener` is subscribed. No allocation, no work. Metrics only become active when something listens.

## Strategy callbacks vs metrics

Two complementary systems:

- **Callbacks** (`OnRetry`, `OnTimeout`, etc.) — inline, per-pipeline, for custom logic (logging, alerting, request mutation)
- **Metrics** — global, aggregated, for observability dashboards and alerting systems

Both fire on the same events. Use callbacks for per-request decisions. Use metrics for aggregate monitoring.
