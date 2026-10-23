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
| `Func<Args, ValueTask>` callbacks (`OnRetry`, `OnOpened`, ...) | `ResilienceEventHandler<TArgs>` | Converts implicitly from `Action<TArgs>` *or* `Func<TArgs, ValueTask>`, so a synchronous callback (logging, counters) runs inline with no `ValueTask` allocated. The delegate must be named, though: it is a wrapper struct, not a delegate type, so a bare lambda is `CS1660` and a method group is `CS0428`. Polly's plain-delegate properties take a lambda directly. See [future-plans.md](future-plans.md) #62. |
| `FallbackAction` (always a delegate) | `FallbackAction<TResult>` | Implicit conversion from a constant value, a sync factory, *or* an async factory — pick whichever fits, the compiler resolves it. |
| `ResiliencePipelineRegistry<TKey>` | `ResiliencePipelineRegistry<TKey>` (same name) | Resilion additionally exposes `IPipelineProvider<TKey>` — a read-only view for consumers that only retrieve pipelines, never register them. |
| `Microsoft.Extensions.Http.Resilience` | `Resilion.Http` | `AddStandardResilienceHandler()`, `AddStandardHedgingHandler()` and `AddResilienceHandler(key, ...)` on `IHttpClientBuilder`, under the same names. See [HTTP](#http-migrating-from-microsoftextensionshttpresilience) below. |
| `AddResilienceEnricher()` | *(not yet available)* | See "Not yet available" below. |
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
- **Callbacks accept a sync `Action`, but you must name the delegate.** `ResilienceEventHandler<TArgs>`'s implicit conversions mean a synchronous callback (the 90% case) doesn't pay for `ValueTask` wrapping, and async callbacks still work when genuinely needed. The cost is brevity: because the handler is a wrapper struct rather than a delegate type, implicit conversion can't give a lambda a target type, so `OnRetry = e => ...` fails with `CS1660` and `OnRetry = LogRetry` with `CS0428`. Write a typed local, an inline cast, or `new Action<TArgs>(...)`:

  ```csharp
  // Polly — a lambda goes straight in
  var polly = new ResiliencePipelineBuilder()
      .AddRetry(new Polly.Retry.RetryStrategyOptions
      {
          OnRetry = args => { logger.LogWarning("retry {Attempt}", args.AttemptNumber); return default; },
      })
      .Build();
  ```

  ```csharp
  // Resilion — the delegate type has to be named somewhere
  var onRetry = new Action<RetryAttemptEvent>(e => logger.LogWarning("retry {Attempt}", e.AttemptNumber));

  var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
  {
      MaxRetryAttempts = 3,
      OnRetry = onRetry,
  }));
  ```

  Polly wins on brevity here; Resilion wins on not allocating a `ValueTask` for a log line. Tracked as [future-plans.md](future-plans.md) #62.
- **`FallbackAction<T>` implicit conversion** — a constant, a sync factory (`Func<Outcome<T>, T>`), or an async factory (`Func<Outcome<T>, ValueTask<T>>`) all convert implicitly. Pick whichever shape your fallback logic naturally has.
- **True sync execution, not sync-over-async.** Resilion's `Execute()` path uses real synchronous primitives (`Thread.Sleep`, `WaitHandle`) rather than blocking on an async path. This matters for ASP.NET Framework, WinForms/WPF, and any code that can't safely use `.GetAwaiter().GetResult()`.
- **`Pipeline.Create()` static factory** vs. `new ResiliencePipelineBuilder().Build()` — a small ergonomic difference, but it's the idiomatic entry point throughout Resilion's docs and samples.

## HTTP: migrating from `Microsoft.Extensions.Http.Resilience`

`Resilion.Http` covers the same ground, under the same method names, on the same
`IHttpClientBuilder`. Most registrations port across as-is; the way you *configure* them does not.
See [http.md](http.md) for the package's own documentation.

```bash
dotnet add package Resilion.Http
```

| `Microsoft.Extensions.Http.Resilience` | `Resilion.Http` | Notes |
|-------|----------|-------|
| `AddStandardResilienceHandler()` | `AddStandardResilienceHandler()` | Same name, same receiver. Same five strategies, outermost first: rate limiter, total request timeout, retry, circuit breaker, per-attempt timeout. |
| `AddStandardHedgingHandler()` | `AddStandardHedgingHandler()` | Hedging in place of retry. Returns `IHttpClientBuilder` for chaining, not a routing builder — see the differences below. |
| `AddResilienceHandler(name, builder => ...)` | `AddResilienceHandler(key, pb => ...)` | `pb` is a `PipelineBuilder<HttpResponseMessage>`. The key must be unique within the client; registering it twice throws at registration time rather than on first resolve. |
| `HttpStandardResilienceOptions.{RateLimiter, TotalRequestTimeout, Retry, CircuitBreaker, AttemptTimeout}` | `HttpStandardResilienceOptions` — the same five property names | The container is a mutable class in both libraries. Each *leg* is a Resilion options `record`, so you assign a new one instead of mutating it — see below. |
| `HttpStandardHedgingOptions.Hedging` | `HttpStandardHedgingOptions.Hedging` | A `HedgingStrategyOptions<HttpResponseMessage>` record. |
| `HttpClientResiliencePredicates.IsTransientHttpFailure` | `HttpResiliencePredicates.IsTransient` | Same definition — 5xx, 408, 429, `HttpRequestException`, `TimeoutRejectedException`. `IsTransientStatusCode` and `IsTransientException` are exposed separately. |
| `ResilienceHandlerContext.ServiceProvider` | the `(options, IServiceProvider)` overload | `AddStandardResilienceHandler((o, sp) => ...)`, `AddStandardHedgingHandler((o, sp) => ...)` and `AddResilienceHandler(key, (pb, sp) => ...)` all exist. That provider is the **root** one, so resolve singletons only — a scoped service taken from it is a captive dependency. |

### The one that will not compile: `with`, not mutation

This is the change most likely to stop a migration dead. Polly's leg options are mutable classes,
so you set properties on them in place. Resilion's are `record` types with `init` properties, so
you replace the record:

```csharp
// Polly
services.AddHttpClient("api").AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 5;
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
});
```

```csharp
// Resilion
services.AddHttpClient("api").AddStandardResilienceHandler(o =>
{
    o.Retry = o.Retry with { MaxRetryAttempts = 5 };
    o.AttemptTimeout = o.AttemptTimeout with { Timeout = TimeSpan.FromSeconds(3) };
});
```

Replacing a leg wholesale is safe too. A null `ShouldHandle` has
`HttpResiliencePredicates.IsTransient` substituted when the pipeline is built, so this still
retries 5xx rather than silently dropping to the exception-only default:

```csharp
o.Retry = new RetryStrategyOptions<HttpResponseMessage> { MaxRetryAttempts = 5 };
```

Set `ShouldHandle` explicitly to override that — including to handle exceptions only.

### Differences that change behaviour

- **The rate limiter is off by default.** `Microsoft.Extensions.Http.Resilience` 8.x enabled a
  1000-permit concurrency limiter; 9.x removed it again. Resilion never enables one, because a
  `RateLimiter`'s lifetime belongs to whoever created it and the pipeline never disposes one. If
  you were relying on the 8.x default, register a limiter and opt in — see
  [http.md](http.md#rate-limiting).
- **`Retry-After` is not honoured.** Both libraries retry 429 and 503 by default, but Resilion
  retries them on blind exponential backoff and ignores the header. `RetryDelay.Custom` receives
  only the attempt number, so closing this needs a core change — [future-plans.md](future-plans.md)
  #63. Until then, handle 429 yourself if the server publishes a retry window you care about. This
  is the gap most likely to matter.
- **Synchronous `HttpClient.Send` throws `NotSupportedException`.** Polly's handler supports it.
  Silently bypassing the pipeline would be worse than failing, so Resilion fails.
- **No per-endpoint hedging routing.** Polly's `AddStandardHedgingHandler()` returns a builder that
  can route attempts to different authorities. Every Resilion hedged attempt targets the same URI,
  and the hedging handler always clones the request, so it always buffers the body.
- **No `IConfiguration` binding.** Polly binds `HttpStandardResilienceOptions` from configuration;
  Resilion configures in code only. The `(o, sp)` overload is the workaround — read your own
  options from the provider and assign.

Exceptions behave the same in both: `TimeoutRejectedException`, `CircuitBrokenException` and
`RateLimitRejectedException` propagate unchanged rather than being wrapped in
`HttpRequestException`, and a 5xx that survives every retry is returned, not thrown.

## Not yet available

Resilion is younger than Polly and doesn't yet have feature parity in every area. If you depend
on one of these today, Polly remains the better fit until Resilion catches up:

| Feature | Status |
|---------|--------|
| Chaos engineering (Simmy equivalent) | Planned — `Resilion.Chaos` |
| Dedicated testing package (`Resilion.Testing`) | Deferred — `CircuitBreakerStateProvider` and a public `Pipeline.Name` shipped instead |
| `IConfiguration` binding for strategy options | Planned, lower priority |
| `Retry-After` support on HTTP retries | Planned — future-plans.md #63 |
| Synchronous `HttpClient.Send` through the pipeline | Unscheduled — needs future-plans.md #66 |
| Per-endpoint hedging routing | Unscheduled |
| Telemetry enrichment (`MeteringEnricher`, raw listeners) | Planned, lower priority |

See [future-plans.md](future-plans.md) for the full roadmap and [comparison-with-polly.md](comparison-with-polly.md) for an honest side-by-side on where each library is stronger today.
