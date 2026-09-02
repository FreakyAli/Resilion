# Pipelines

Pipelines compose multiple strategies into a single execution unit. Strategies execute outermost to innermost — the first strategy added is the first to see each call.

## Creating pipelines

```csharp
// Non-generic — strategies react to exceptions only
var pipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions()));

// Typed — strategies can react to result values too
var typed = Pipeline.Create<HttpResponseMessage>(b => b
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = outcome => outcome.Exception is HttpRequestException
            || (outcome.TryGetResult(out var r) && (int)r.StatusCode >= 500),
    })
    .AddTimeout(TimeSpan.FromSeconds(5)));
```

## Strategy ordering

Order matters. A call flows inward (outermost first), then results flow back outward.

### Canonical order

```csharp
Pipeline.Create(b => b
    .AddRateLimiter(...)       // 1. Shed load before spending effort
    .AddTimeout(30s)           // 2. Total timeout across all retries
    .AddRetry(...)             // 3. Retry failures from inner strategies
    .AddCircuitBreaker(...)    // 4. Track per-attempt success/failure
    .AddTimeout(5s));          // 5. Per-attempt timeout
```

### Why order matters

Same strategies, different order, different behavior:

```csharp
// Timeout OUTSIDE retry = one 10s budget for ALL attempts
b.AddTimeout(TimeSpan.FromSeconds(10));
b.AddRetry(...);

// Timeout INSIDE retry = each attempt gets 10s
b.AddRetry(...);
b.AddTimeout(TimeSpan.FromSeconds(10));
```

## Pipeline composition

Combine pre-built pipelines with `AddPipeline`:

```csharp
var retryPolicy = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
}));

var timeoutPolicy = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));

var combined = Pipeline.Create(b => b
    .AddPipeline(timeoutPolicy)    // Timeout outermost
    .AddPipeline(retryPolicy));    // Retry innermost
```

`AddPipeline` stores the source pipeline as a delegated component using delegated composition. It records `StrategyType.Custom` rather than flattening inner strategies. The inner strategies remain encapsulated and are not individually visible to ordering validators or strategy introspection.

## Empty pipelines

```csharp
Pipeline.Empty              // No-op, passes through
Pipeline<string>.Empty      // Typed no-op
```

Useful for testing or conditional composition:

```csharp
var pipeline = useResilience
    ? Pipeline.Create(b => b.AddRetry().AddTimeout(TimeSpan.FromSeconds(10)))
    : Pipeline.Empty;
```

## Execution methods

### Async (primary)

```csharp
// With state parameter — zero closure allocation
await pipeline.ExecuteAsync(
    static (state, ct) => state.client.GetStringAsync(state.url, ct),
    (client: httpClient, url: "https://api.example.com"),
    cancellationToken);

// Without state — simpler but allocates closure
await pipeline.ExecuteAsync(async ct =>
    await httpClient.GetStringAsync("https://api.example.com", ct));

// Void
await pipeline.ExecuteAsync(async ct =>
    await httpClient.PostAsync("https://api.example.com", content, ct));
```

### Sync

```csharp
// With state
var result = pipeline.Execute(
    static (state, ct) => state.Compute(state.input),
    (Compute: myFunc, input: 42));

// Without state
var result = pipeline.Execute(ct => ComputeSync(ct));

// Void
pipeline.Execute(ct => DoWork(ct));
```

### Outcome-based (no throwing)

```csharp
var context = ResilienceContextPool.Shared.Rent(cancellationToken);
try
{
    var outcome = await pipeline.ExecuteOutcomeAsync(
        static (state, ctx) => /* ... */,
        myState,
        context);

    if (outcome.IsSuccess)
        Console.WriteLine(outcome.Result);
    else
        Console.WriteLine(outcome.Exception);
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}
```

## Disposal

Pipelines implement `IDisposable`. Dispose when strategies hold resources (circuit breaker timers). Pipelines from `AddPipeline` composition share ownership — dispose composed pipelines separately.

## Immutability and thread safety

Pipelines are sealed, immutable after construction, and thread-safe. Build once, cache as a `static readonly` or singleton, reuse across threads.
