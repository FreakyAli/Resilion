# Custom Strategies

Extend Resilion with your own resilience logic.

## Delegate-based (typed pipelines)

Quickest way — zero files, inline middleware:

```csharp
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b
    .AddStrategy("cache-check", async (context, next) =>
    {
        // Check cache before calling downstream
        if (cache.TryGet(context.OperationKey, out HttpResponseMessage cached))
            return Outcome<HttpResponseMessage>.FromResult(cached);

        // Execute downstream
        var outcome = await next(context);

        // Cache successful responses
        if (outcome.TryGetResult(out var result) && result.IsSuccessStatusCode)
            cache.Set(context.OperationKey, result);

        return outcome;
    })
    .AddRetry(...)
    .AddTimeout(TimeSpan.FromSeconds(5)));
```

Delegate signature: `Func<ResilienceContext, Func<..., ValueTask<Outcome<T>>>, ValueTask<Outcome<T>>>` — standard middleware pattern.

Only available on typed `PipelineBuilder<T>`.

## Class-based (any pipeline)

For reusable strategies, inherit from `Strategy` (any result type) or `Strategy<TResult>` (specific type):

### Non-generic strategy

Works with any `TResult`. Good for cross-cutting concerns (logging, tracing, metrics).

```csharp
public sealed class LoggingStrategy : Strategy
{
    private readonly ILogger _logger;

    public LoggingStrategy(ILogger logger) => _logger = logger;

    protected override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        _logger.LogDebug("Executing {Op}", context.OperationKey);
        var sw = Stopwatch.StartNew();

        var outcome = await callback(context).ConfigureAwait(false);

        _logger.LogDebug("Completed {Op} in {Elapsed}ms, Success={S}",
            context.OperationKey, sw.ElapsedMilliseconds, outcome.IsSuccess);

        return outcome;
    }

    // Optional: override for true sync support
    protected override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        _logger.LogDebug("Executing {Op}", context.OperationKey);
        var sw = Stopwatch.StartNew();

        var outcome = callback(context);

        _logger.LogDebug("Completed {Op} in {Elapsed}ms, Success={S}",
            context.OperationKey, sw.ElapsedMilliseconds, outcome.IsSuccess);

        return outcome;
    }
}

// Usage:
Pipeline.Create(b => b
    .AddStrategy(new LoggingStrategy(logger))
    .AddRetry(...)
    .AddTimeout(TimeSpan.FromSeconds(10)));
```

### Typed strategy

Bound to a specific result type. Can inspect and modify results.

```csharp
public sealed class CachingStrategy<T> : Strategy<T>
{
    private readonly ICache<T> _cache;

    public CachingStrategy(ICache<T> cache) => _cache = cache;

    protected override async ValueTask<Outcome<T>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<T>>> callback,
        ResilienceContext context)
    {
        if (context.OperationKey is not null
            && _cache.TryGet(context.OperationKey, out var cached))
        {
            return Outcome<T>.FromResult(cached);
        }

        var outcome = await callback(context).ConfigureAwait(false);

        if (outcome.TryGetResult(out var result) && context.OperationKey is not null)
        {
            _cache.Set(context.OperationKey, result);
        }

        return outcome;
    }
}
```

## Key points

- `callback` is the next strategy in the chain (or the user delegate if this is the innermost strategy)
- Always call `callback` unless you're short-circuiting (e.g., cache hit)
- Use `ConfigureAwait(false)` on awaits in library code
- Override `Execute` for true sync support; default falls back to sync-over-async
- Implement `IDisposable` (inherited from `Strategy`) if you hold resources
- Pass `context.CancellationToken` to any async operations you call

## Compared to Polly

Polly requires four artifacts for a custom strategy: options class, arguments struct, strategy class, extension method. Resilion requires one class (or zero files for delegate-based).
