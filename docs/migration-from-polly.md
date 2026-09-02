# Migrating from Polly

A concept-by-concept and code-by-code map from Polly v8 (`Polly.Core`) to Resilion. This is the
fastest path if you already know Polly.

## Concept mapping

| Polly | Resilion | Notes |
|-------|----------|-------|
| `ResiliencePipeline` | `Pipeline` / `Pipeline<TResult>` | Resilion splits non-generic (exception-only) and typed (result-based) pipelines into distinct types instead of one generic-only pipeline. |
| `ResiliencePipelineBuilder` / `ResiliencePipelineBuilder<T>` | `PipelineBuilder` / `PipelineBuilder<TResult>` | Same split as above. Both inherit shared behavior from `PipelineBuilderBase`. |
| `new ResiliencePipelineBuilder().Add...().Build()` | `Pipeline.Create(b => b.Add...)` | Static factory instead of `new` + fluent `Build()`. |
| `RetryStrategyOptions` (Delay + BackoffType + UseJitter + DelayGenerator) | `RetryStrategyOptions` (`Delay = RetryDelay.___`) | `RetryDelay` is a discriminated union (`Constant`/`Linear`/`Exponential`/`Custom`) — mutually exclusive by construction, no silent overrides between properties. |
| `CircuitBreakerStrategyOptions.FailureRatio` | `CircuitBreakerStrategyOptions.FailureRatioThreshold` | Same concept, renamed for clarity. |
| `Func<Args, ValueTask>` callbacks (`OnRetry`, `OnOpened`, ...) | `ResilienceEventHandler<TArgs>` | Accepts a plain sync `Action<TArgs>` *or* an async `Func<TArgs, ValueTask>` via implicit conversion — no forced `ValueTask` wrapping for the common synchronous case (logging, counters). |
| `FallbackAction` (always a delegate) | `FallbackAction<TResult>` | Implicit conversion from a constant value, a sync factory, *or* an async factory — pick whichever fits, the compiler resolves it. |
| `ResiliencePipelineRegistry<TKey>` | `ResiliencePipelineRegistry<TKey>` (same name) | Resilion additionally exposes `IPipelineProvider<TKey>` — a read-only view for consumers that only retrieve pipelines, never register them. |
| `AddResilienceEnricher()` / `Microsoft.Extensions.Http.Resilience` | *(not yet available)* | See "Not yet available" below. |
| Sync via `.Execute(...)` (sync-over-async under the hood for some strategies) | `Execute(...)` (true sync — `Thread.Sleep`/`WaitHandle`, no `Task` wrapping) | See `docs/architecture.md`'s "Sync vs async" section. |

## Before/after: the five most common patterns

### 1. Basic retry with exponential backoff

```csharp
// Polly
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new Polly.Retry.RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
    })
    .Build();
```

```csharp
// Resilion
var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
    UseJitter = true,
}));
```

### 2. Retry + circuit breaker composite

```csharp
// Polly
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 10,
    })
    .Build();
```

```csharp
// Resilion — same ordering matters here too: Retry outside CircuitBreaker,
// so the breaker only sees the final outcome of each retried call, not every attempt.
var pipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatioThreshold = 0.5,
        MinimumThroughput = 10,
    }));
```

### 3. Timeout (total + per-attempt)

```csharp
// Polly
var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(5))
    .Build();
```

```csharp
// Resilion — identical shape
var pipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))   // Total timeout across all retries
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(5)));  // Per-attempt timeout
```

### 4. DI registration and injection

```csharp
// Polly
services.AddResiliencePipeline("http-api", builder => builder
    .AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 3 }));

var pipeline = serviceProvider.GetRequiredService<ResiliencePipelineProvider<string>>()
    .GetPipeline("http-api");
```

```csharp
// Resilion
services.AddResiliencePipeline("http-api", b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 }));

var pipeline = serviceProvider.GetRequiredService<IPipelineProvider<string>>()
    .GetPipeline("http-api");
```

### 5. Typed pipeline with result-based fallback

```csharp
// Polly
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddFallback(new Polly.Fallback.FallbackStrategyOptions<HttpResponseMessage>
    {
        FallbackAction = _ => Outcome.FromResultAsValueTask(cachedResponse),
    })
    .Build();
```

```csharp
// Resilion — FallbackAction accepts a constant, a sync factory, or an async factory directly,
// via implicit conversion, instead of one delegate shape for every case.
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddFallback(
    new FallbackStrategyOptions<HttpResponseMessage>
    {
        FallbackAction = cachedResponse, // implicit conversion from a constant value
    }));
```

## Key differences to internalize

- **`RetryDelay` discriminated union vs. Polly's `BackoffType` enum + separate properties.** In Polly, `Delay`, `BackoffType`, `UseJitter`, and `DelayGenerator` are independent properties that can be set inconsistently (e.g. `DelayGenerator` set alongside `BackoffType`, with unclear precedence). In Resilion, `RetryDelay.Constant/Linear/Exponential/Custom` are mutually exclusive by construction — there's exactly one way to configure the delay strategy.
- **Callbacks accept sync `Action` directly.** `ResilienceEventHandler<TArgs>`'s implicit conversions mean a synchronous callback (the 90% case) doesn't pay for `ValueTask` wrapping. Async callbacks still work when genuinely needed.
- **`FallbackAction<T>` implicit conversion** — a constant, a sync factory (`Func<Outcome<T>, T>`), or an async factory (`Func<Outcome<T>, ValueTask<T>>`) all convert implicitly. Pick whichever shape your fallback logic naturally has.
- **True sync execution, not sync-over-async.** Resilion's `Execute()` path uses real synchronous primitives (`Thread.Sleep`, `WaitHandle`) rather than blocking on an async path. This matters for ASP.NET Framework, WinForms/WPF, and any code that can't safely use `.GetAwaiter().GetResult()`.
- **`Pipeline.Create()` static factory** vs. `new ResiliencePipelineBuilder().Build()` — a small ergonomic difference, but it's the idiomatic entry point throughout Resilion's docs and samples.

## Not yet available

Resilion is younger than Polly and doesn't yet have feature parity in every area. If you depend
on one of these today, Polly remains the better fit until Resilion catches up:

| Feature | Status |
|---------|--------|
| `IHttpClientFactory` integration (`AddStandardResilienceHandler()`) | Planned — `Resilion.Http`, highest priority post-v1.0 |
| Chaos engineering (Simmy equivalent) | Planned — `Resilion.Chaos` |
| Dynamic reload via `IOptionsMonitor` | Planned |
| Dedicated testing package (`Resilion.Testing`) | Planned |
| `PredicateBuilder<T>` fluent API | Planned, lower priority |
| `IConfiguration` binding for strategy options | Planned, lower priority |
| Telemetry enrichment (`MeteringEnricher`, raw listeners) | Planned, lower priority |

See [future-plans.md](future-plans.md) for the full roadmap and [comparison-with-polly.md](comparison-with-polly.md) for an honest side-by-side on where each library is stronger today.
