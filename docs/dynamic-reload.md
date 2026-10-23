# Dynamic reload

Rebuild a named pipeline when its configuration changes, without restarting the process. Lives in
the `Resilion.Extensions` package.

> **The one rule that makes this work:** with reload enabled, resolve the pipeline **per call**.
> Never cache a `Pipeline` in a field. Everything below follows from that.

## Basic usage

Bind a pipeline to an options type and it rebuilds whenever `IOptionsMonitor<TOptions>` reports a
change — which includes `appsettings.json` edits, since the default configuration providers watch
the file.

```csharp
using Resilion;
using Resilion.Extensions;

services.Configure<ApiResilienceOptions>(config.GetSection("Resilience:Api"));

services.AddResiliencePipeline<ApiResilienceOptions>("api", (builder, options) =>
{
    builder.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = options.MaxRetries });
    builder.AddTimeout(options.Timeout);
});
```

The `configure` delegate is stored, not its result. Every rebuild re-runs it against
`IOptionsMonitor.CurrentValue`, so it reads **fresh** options each time — it must not capture a
snapshot of them.

See [configuration.md](configuration.md) for binding the built-in strategy options straight from
`IConfiguration`.

### Named options

Use the `optionsName` overload to bind several pipelines to different sections of the same options
type:

```csharp
services.Configure<ApiResilienceOptions>("api",     config.GetSection("Resilience:Api"));
services.Configure<ApiResilienceOptions>("billing", config.GetSection("Resilience:Billing"));

services.AddResiliencePipeline<ApiResilienceOptions>("api",     "api",     (b, o) => ...);
services.AddResiliencePipeline<ApiResilienceOptions>("billing", "billing", (b, o) => ...);
```

## Consuming a reloadable pipeline

**Correct** — resolve on each call, so you always get the current instance:

```csharp
public sealed class ApiClient(IPipelineProvider<string> pipelines)
{
    public ValueTask<string> GetAsync(CancellationToken ct) =>
        pipelines.GetPipeline("api").ExecuteAsync(FetchAsync, ct);
}
```

**Wrong** — this caches the pipeline that existed when the class was constructed, and will use it
forever:

```csharp
public sealed class ApiClient
{
    private readonly Pipeline _pipeline;   // never updates

    public ApiClient(IPipelineProvider<string> pipelines) =>
        _pipeline = pipelines.GetPipeline("api");
}
```

`GetPipeline` is a dictionary read plus — after an invalidation — one build. It is not a call worth
caching around.

## Reload and keyed injection are mutually exclusive

Keyed DI resolution works:

```csharp
public sealed class ApiClient([FromKeyedServices("api")] Pipeline pipeline) { ... }
```

but **a keyed pipeline never observes a reload.** The keyed registration is a singleton, so the
container resolves it once and caches it for its own lifetime, and your constructor parameter
caches it again. Neither ever asks the registry a second time.

This is not a bug to be fixed later; it is what "singleton" means. The two features serve different
cases:

| | Use |
|---|---|
| `[FromKeyedServices("name")] Pipeline` | a pipeline whose configuration is fixed for the process lifetime |
| `IPipelineProvider<string>`, resolved per call | a pipeline that can be reloaded |

Transient keyed registration is **not** an escape hatch: DI tracks and disposes transient
`IDisposable`s resolved from a scope, which would dispose the registry's cached pipeline out from
under every other consumer.

## Reloading from something other than `IOptionsMonitor`

`InvalidatePipeline` is the primitive; the options binding above is one caller of it.

```csharp
public bool InvalidatePipeline(TKey key);
public bool InvalidatePipeline<TResult>(TKey key);
```

It drops the **cached** pipeline and keeps the registered factory, so the next `GetPipeline`
rebuilds. It returns `true` if a cache entry was removed — including one holding a cached build
failure — and `false` for an unknown key or a key registered but never resolved, since in both of
those cases the next `GetPipeline` already builds a current pipeline. Any `IChangeToken` source
works with no extra API:

```csharp
ChangeToken.OnChange(
    () => config.GetReloadToken(),
    () => registry.InvalidatePipeline("api"));
```

The typed and non-generic stores are separate — `InvalidatePipeline<HttpResponseMessage>("api")`
does not affect `InvalidatePipeline("api")`.

## What happens to the old pipeline

**The registry does not dispose a replaced pipeline, deliberately.** It cannot know when the last
execution on it finishes without ref-counting every execute path.

- An execution already running continues on the old pipeline and completes normally. `Pipeline` is
  immutable, and the executing frame holds its own reference, so there is no tearing.
- New calls get the new pipeline.
- The old one is collected once nothing references it. That is safe while no strategy owns an
  unmanaged resource, which is true of every built-in strategy.

If you need to know — to dispose it yourself after a grace period, or to log it — subscribe:

```csharp
services.ConfigureResiliencePipelineRegistry(registry =>
{
    registry.OnPipelineReplaced = args =>
        logger.LogInformation("Rebuilt pipeline {Key} ({ResultType})", args.Key, args.ResultType);
});
```

`PipelineReplacedArgs<TKey>` carries the `Key`, the `ResultType` (`null` for a non-generic
pipeline) and the evicted instance as `IAsyncDisposable`. The callback fires only when a
successfully built pipeline was evicted — evicting a cached build failure returns `true` without
invoking it, because there is no instance to hand over.

Disposing the old pipeline immediately inside that callback is usually wrong, for the in-flight
reason above.

## State is lost on reload, by definition

A rebuilt pipeline is a **new** pipeline. A circuit breaker starts closed with an empty window; a
rate limiter is whatever instance the factory supplies.

This matters most for the circuit breaker: reloading a pipeline resets its memory of recent
failures, so a config change during an incident re-opens traffic to a failing dependency. Reload
the pipelines whose values operators genuinely need to tune at runtime, not every pipeline by
default.

A rate limiter is the exception, and only if you supply the same instance — `RateLimiterStrategyOptions.RateLimiter`
is a caller-owned instance, so resolving a DI singleton inside the factory preserves it across
rebuilds.

## Consistency

Invalidation and retrieval are not serialized against each other. A `GetPipeline` racing a
concurrent `InvalidatePipeline` can return a pipeline built from options that were current
microseconds earlier. This is eventual consistency by design — the alternative is a lock on the
hot path to make a configuration change land a few microseconds sooner.

**A build that throws is cached, exception and all.** If the `configure` delegate throws — bad
values from a reload, a service that was not ready — the `Lazy` holds that exception and every
later `GetPipeline` for that key rethrows *the same* exception without re-running the factory. The
pipeline stays broken until something invalidates it.

With reload wired up that resolves itself: the next options change invalidates the key and the
following resolve rebuilds. Without reload it is permanent for the process lifetime, so validate
configuration where it is bound rather than inside the factory. Tracked as
[#67](future-plans.md).

## See also

- [configuration.md](configuration.md) — binding strategy options from `IConfiguration`
- [pipelines.md](pipelines.md) — pipeline construction and composition
