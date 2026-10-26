# Chaos engineering

Deliberately inject faults, results, latency and custom behaviour into a pipeline so you can find
out whether your resilience configuration actually works. Lives in the `Resilion.Chaos` package.

```text
dotnet add package Resilion.Chaos
```

Zero third-party dependencies — it references `Resilion` and nothing else.

## Read this first: the production guard

**The defaults inject.** `Enabled` is `true` and `InjectionRate` is `0.001`, so a chaos strategy
added to a pipeline starts injecting on roughly one call in a thousand the moment it is built.
That is the right default for a package you add on purpose, and the wrong thing to discover by
accident.

Gate it on the environment, every time:

```csharp
using Resilion;
using Resilion.Chaos;

var pipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddChaosFault(new ChaosFaultStrategyOptions
    {
        EnabledGenerator = _ => !env.IsProduction(),   // <- the guard
        InjectionRate = 0.05,
        FaultGenerator = _ => new HttpRequestException("chaos: simulated transport failure"),
    }));
```

`EnabledGenerator` is evaluated per call, so it also works with a feature flag that flips at
runtime.

## The four strategies

| Strategy | Injects | Use it to test |
|---|---|---|
| `AddChaosFault` | an exception | retry and circuit breaker exception predicates |
| `AddChaosOutcome` | a result *or* a failure | result-based predicates — a 503, an empty payload |
| `AddChaosLatency` | a delay | timeouts, and hedging that should overtake a slow call |
| `AddChaosBehavior` | anything you write | killing a dependency, filling a disk, dropping a connection |

`AddChaosOutcome` is typed-only — it has no `PipelineBuilder` overload. A `Strategy<TResult>` added
to a pipeline executing a different result type cannot run, so an untyped overload would be a trap
with no correct use. The other three are result-agnostic and work on both builders.

## Chaos goes innermost

**Add chaos last.** A chaos strategy placed outside a retry injects its fault before the retry can
see it, so the retry never observes a failure:

```csharp
// Wrong — the fault is thrown before Retry is reached, so Retry never sees it and the
// experiment passes while proving nothing.
Pipeline.Create(b => b
    .AddChaosFault(options)
    .AddRetry(retryOptions));

// Right — Retry wraps the chaos, sees the injected fault, and retries.
Pipeline.Create(b => b
    .AddRetry(retryOptions)
    .AddChaosFault(options));
```

Getting this wrong is worse than a normal misconfiguration because it fails *silently and
positively*: the run completes, nothing errors, and you conclude your resilience works.

Resilion emits an ordering **warning** when a chaos strategy is not innermost. It is a warning
rather than an error because stacking several chaos strategies at the end is legitimate, and a
deliberate mid-pipeline injection between an outer and an inner pipeline is conceivable.

## Testing chaos: the `Randomizer` seam

Injection is a coin flip, which would make every test statistical. `Randomizer` replaces the source
of randomness so it isn't:

```csharp
Randomizer = () => 0.0   // always inject
Randomizer = () => 1.0   // never inject
```

It is consulted on **every** decision with no short-circuit, so those two values hold at any
configured rate. This is the supported way to test a chaos configuration, and it is the same idea
as `PipelineBuilder.TimeProvider` for time — without it, chaos strategies would be as untestable as
a strategy that reads `TimeProvider.System` directly.

`AddChaosLatency` takes `builder.TimeProvider`, so `FakeTimeProvider` drives injected delays:

```csharp
var time = new FakeTimeProvider();
var pipeline = Pipeline.Create(b =>
{
    b.TimeProvider = time;
    b.AddChaosLatency(1.0, TimeSpan.FromSeconds(30));
});

var task = pipeline.ExecuteAsync(ct => CallAsync(ct)).AsTask();
time.Advance(TimeSpan.FromSeconds(30));
await task;
```

## Shared options

Every chaos strategy inherits these:

| Option | Default | Meaning |
|---|---|---|
| `InjectionRate` | `0.001` | probability of injecting, `[0, 1]` |
| `InjectionRateGenerator` | `null` | per-call rate; **overrides** `InjectionRate` |
| `Enabled` | `true` | whether the strategy is active at all |
| `EnabledGenerator` | `null` | per-call check; **overrides** `Enabled` |
| `Randomizer` | `Random.Shared` | source of the injection decision |

**A generator always wins over its static counterpart**, uniformly across all four strategies — the
decision is made in one place so they cannot drift apart.

Out-of-range values are handled differently depending on when they are known. A bad static
`InjectionRate` **throws at `Add*` time**, where the stack trace points at your configuration. A
generator returning something out of range is **clamped**, because it runs mid-execution where an
exception would be indistinguishable from an injected fault.

## Prefer `FaultGenerator` over `Fault`

```csharp
// Preferred
FaultGenerator = _ => new HttpRequestException("chaos"),

// Works, but degrades
Fault = new HttpRequestException("chaos"),
```

`Outcome<T>.FromException` captures with `ExceptionDispatchInfo`, so rethrowing one shared instance
appends to its stack trace on every injection and the traces get progressively less readable.

A A `FaultGenerator` returning `null` skips injection for that call, which lets a generator filter on
the context. The injection counter is not incremented in that case — it counts injections, not
decisions.

## Custom behaviour

```csharp
b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
{
    EnabledGenerator = _ => !env.IsProduction(),
    InjectionRate = 0.01,
    Behavior = _ => { proxy.DropConnections(); return default; },
});
```

`Behavior` is a `ResilienceEventHandler<ChaosGeneratorArgs>` — a delegate returning `ValueTask`,
so a synchronous payload returns `default` and runs inline, and an `async` payload is awaited.

An exception from `Behavior` is **not** caught — it propagates into the pipeline, which is usually
the point.

## Telemetry

Injections increment `resilion.chaos.injections` on the existing `Resilion` meter, so a single
`.AddMeter("Resilion")` picks them up alongside everything else.

| Tag | Value |
|---|---|
| `pipeline.name` | the pipeline's name |
| `operation.key` | the execution's operation key |
| `chaos.strategy` | `fault`, `outcome`, `latency` or `behavior` |

The tag key is available as `ResilionTelemetry.ChaosStrategyTag`. See
[telemetry.md](telemetry.md).

## Synchronous execution

All four strategies implement real synchronous paths. One caveat: injected latency on the sync path
waits on `CancellationToken.WaitHandle`, which has no `TimeProvider` equivalent, so a
`FakeTimeProvider` cannot drive it — the same limitation as synchronous retry delays. Test sync
latency with a short real delay, or test the async path.

## Known limitations

- **Injection rates cannot come from configuration.** The options are set in code; binding them
  from `appsettings.json` is tracked as [#68](future-plans.md).
- **No chaos hook on `AddStandardResilienceHandler`.** The standard HTTP pipeline is fixed, so
  injecting into it means composing the pipeline yourself with `AddResilienceHandler`. Tracked as
  [#69](future-plans.md).
- **Synchronous injected latency is not fake-clock testable**, as described above. Tracked as
  [#9](future-plans.md).

## See also

- [pipelines.md](pipelines.md) — strategy ordering in general
- [testing.md](testing.md) — `FakeTimeProvider` and telemetry capture
- [telemetry.md](telemetry.md) — the full instrument list
