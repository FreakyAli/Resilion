# HttpClient Integration

`Resilion.Http` wires a Resilion pipeline into `HttpClient` through `IHttpClientFactory`.

```bash
dotnet add package Resilion.Http
```

## Quick start

```csharp
services.AddHttpClient("api").AddStandardResilienceHandler();
```

That gives the client, outermost first:

| | Strategy | Default |
|---|---|---|
| 1 | Rate limiter | **off** — see [Rate limiting](#rate-limiting) |
| 2 | Total request timeout | 30 seconds, across all attempts |
| 3 | Retry | 3 attempts, jittered exponential backoff from 2s |
| 4 | Circuit breaker | 10% failure ratio over 30s, minimum throughput 100, break 5s |
| 5 | Per-attempt timeout | 10 seconds |

Retry and the circuit breaker treat a call as failed when it throws `HttpRequestException` or
`TimeoutRejectedException`, or returns 5xx, 408, or 429.

## Adjusting the defaults

Use a `with` expression to change one thing, rather than replacing the record:

```csharp
services.AddHttpClient("api").AddStandardResilienceHandler(o =>
{
    o.Retry = o.Retry with { MaxRetryAttempts = 5 };
    o.AttemptTimeout = new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(3) };
});
```

Replacing a record wholesale is also safe. If you leave `ShouldHandle` null, the HTTP transient
predicate is substituted when the pipeline is built, so this still retries 5xx:

```csharp
o.Retry = new RetryStrategyOptions<HttpResponseMessage> { MaxRetryAttempts = 5 };
```

Set `ShouldHandle` explicitly to override that — including to handle exceptions only.

There is an overload taking the service provider, for pulling configuration or loggers in:

```csharp
services.AddHttpClient("api").AddStandardResilienceHandler((o, sp) =>
    o.Retry = o.Retry with { MaxRetryAttempts = sp.GetRequiredService<IOptions<ApiOptions>>().Value.Retries });
```

That provider is the **root** one. Resolving a scoped service from it is a captive dependency;
resolve singletons only.

### Validation

`AttemptTimeout` longer than `TotalRequestTimeout` throws — it could never fire. Options are
validated when the pipeline is first built, which happens when `IHttpClientFactory` constructs the
handler chain, so a misconfiguration surfaces at `CreateClient` rather than on the first send.

## Rate limiting

Off by default, which differs from `Microsoft.Extensions.Http.Resilience` 8.x (it enabled a
1000-permit concurrency limiter; 9.x removed it again).

A `RateLimiter`'s lifetime belongs to whoever created it — the pipeline never disposes one — so
enabling it by default would mean this package silently owning a limiter and throttling any
application above its permit count. Opt in and keep ownership:

```csharp
services.AddSingleton(new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 1000,
    QueueLimit = 0,
}));

services.AddHttpClient("api").AddStandardResilienceHandler((o, sp) =>
    o.RateLimiter = new RateLimiterStrategyOptions
    {
        RateLimiter = sp.GetRequiredService<ConcurrencyLimiter>(),
    });
```

## Hedging

For latency-sensitive calls to **idempotent** endpoints. Replaces retry with hedging: if the first
attempt has not answered within the delay, a second is issued and the first answer wins.

```csharp
services.AddHttpClient("api").AddStandardHedgingHandler(o =>
    o.Hedging = o.Hedging with { MaxHedgedAttempts = 3, HedgingDelay = TimeSpan.FromSeconds(1) });
```

`HedgingDelay` selects the mode: `TimeSpan.Zero` fires every attempt at once, a positive value
waits that long unless an earlier attempt finishes first, and `Timeout.InfiniteTimeSpan` runs them
strictly in sequence.

Two limits: every attempt targets the same URI — per-endpoint routing is not supported — and this
handler **always clones the request**, so it always buffers the body in memory. Don't use it for
large or streaming uploads.

## Custom pipelines

```csharp
services.AddHttpClient("api").AddResilienceHandler("my-pipeline", pb => pb
    .AddTimeout(TimeSpan.FromSeconds(20))
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 2,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode == HttpStatusCode.ServiceUnavailable),
    }));
```

Several handlers can be added to one client under different keys; they nest in registration order,
outermost first. Registering the same key twice on the same client throws at registration time.

## Request replay

Controls whether each attempt sends your request or a copy.

| Mode | Behaviour | Use when |
|---|---|---|
| `ReuseRequest` *(default)* | Every attempt sends your original instance. No buffering, no overhead. | Content is replayable, or there is none |
| `CloneRequest` | Body is buffered once; every attempt gets an independent clone. | Content is not replayable, or an inner handler mutates the request |

Replayable content means `StringContent`, `ByteArrayContent`, `JsonContent`,
`FormUrlEncodedContent`, or no content. `StreamContent` over a non-seekable stream **cannot** be
sent twice, and an inner handler that *adds* rather than sets a header — some auth handlers do —
throws on the second attempt. Either case needs `CloneRequest`:

```csharp
services.AddHttpClient("api")
    .AddStandardResilienceHandler(o => o.RequestReplay = HttpRequestReplay.CloneRequest);
```

`CloneRequest` buffers the **entire** body in memory. The hedging handler always clones, because
concurrent attempts cannot share one `HttpRequestMessage`.

## Response disposal

Responses from attempts that lose — a retried 500, a hedged straggler — are disposed for you, as
soon as the next attempt starts where attempts cannot overlap. An undisposed response holds its
connection, so without this a retry-heavy client exhausts the pool.

The response you receive is yours; dispose it as usual.

## What to catch

Resilion's exceptions propagate unchanged, matching Polly's handler. They are **not** translated
into `HttpRequestException` or `TaskCanceledException`:

| Exception | Means |
|---|---|
| `TimeoutRejectedException` | An attempt, or the whole operation, exceeded its timeout |
| `CircuitBrokenException` | The circuit is open; the call was rejected without being sent |
| `RateLimitRejectedException` | The rate limiter rejected the call |
| `HttpRequestException` | A transport failure that survived all retries |

A 5xx that survives every retry is **returned**, not thrown — it is a response.

Generated clients that only catch `HttpRequestException` will see types they do not expect.

## Telemetry

Metrics and spans carry `pipeline.name = "<client>/<kind>"` (for example `api/standard`) and
`operation.key` set to the HTTP method. The method, not the URI — a URI would make the tag's
cardinality unbounded. See [telemetry.md](telemetry.md).

From a strategy callback, `context.GetRequestMessage()` returns the current attempt's request.

## Testing

Register a `TimeProvider` and the whole pipeline uses it:

```csharp
var time = new FakeTimeProvider();
services.AddSingleton<TimeProvider>(time);
services.AddHttpClient("api")
    .ConfigurePrimaryHttpMessageHandler(() => stubHandler)
    .AddStandardResilienceHandler();
```

Setting `o.TimeProvider` explicitly wins over the container. See [testing.md](testing.md).

## Handler ordering

`AddHttpMessageHandler` composes outermost-first. A handler that must run **per attempt** — token
refresh, per-try logging — goes *after* the resilience handler; one that must run **once** goes
before it.

## Known limitations

- **No `Retry-After` support.** 429 and 503 are retried on blind exponential backoff, ignoring any
  `Retry-After` header. `RetryDelay.Custom` only receives the attempt number, so this needs a core
  change — see [future-plans.md](future-plans.md).
- **No synchronous `HttpClient.Send`.** It throws `NotSupportedException`. Silently bypassing the
  pipeline would be worse, and a correct sync path needs a synchronous outcome-returning execute
  in core.
- **No per-endpoint hedging.** All hedged attempts target the same URI.
- **No per-request configuration.** The pipeline is built once per client and cached.
