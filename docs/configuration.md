# Configuration binding

Strategy options bound from `appsettings.json`, `IConfiguration`, environment variables, or any
other configuration source. Lives in the `Resilion.Extensions` package, namespace
`Resilion.Extensions.Configuration`.

The feature is a small one by design: a layer of bindable POCOs that project into the real options
records. It does **not** build pipelines from JSON — see [What is not supported](#what-is-not-supported-and-why).

## Why there is a separate DTO layer

The options records are `sealed record` types with `init`-only members, and `RetryDelay` is a
closed union with a private constructor. Neither shape is bindable: `ConfigurationBinder` needs a
parameterless constructor and settable properties.

That is not an accident to work around. `RetryDelay` exists *because* Polly exposes four
independently-settable delay properties that can contradict each other
(see [comparison-with-polly.md](comparison-with-polly.md)). Making the union itself bindable would
put the contradiction straight back.

So the binding types are a separate, one-way funnel:

| Bindable DTO | Projects into |
|---|---|
| `RetryStrategyConfiguration` | `RetryStrategyOptions`, `RetryStrategyOptions<TResult>` |
| `RetryDelayConfiguration` | `RetryDelay` |
| `TimeoutStrategyConfiguration` | `TimeoutStrategyOptions` |
| `CircuitBreakerStrategyConfiguration` | `CircuitBreakerStrategyOptions`, `CircuitBreakerStrategyOptions<TResult>` |
| `HedgingStrategyConfiguration` | `HedgingStrategyOptions<TResult>` |

Each DTO's defaults mirror the options record it projects into, exactly. **Binding nothing is
identical to passing no options at all.** Drift between the two would be a silent behaviour change,
so `StrategyConfigurationDefaultsTests` asserts each pair field by field.

## A complete example

```json
{
  "Resilience": {
    "Api": {
      "Retry": {
        "MaxRetryAttempts": 5,
        "UseJitter": true,
        "MaxDelay": "00:00:30",
        "Delay": {
          "Kind": "Exponential",
          "BaseDelay": "00:00:02"
        }
      },
      "Timeout": {
        "Timeout": "00:00:10"
      }
    }
  }
}
```

```csharp
using Resilion;
using Resilion.Extensions;
using Resilion.Extensions.Configuration;

services.Configure<RetryStrategyConfiguration>(config.GetSection("Resilience:Api:Retry"));

services.AddResiliencePipeline<RetryStrategyConfiguration>("api", (builder, retry) =>
{
    builder.AddRetry(retry.ToOptions());
    builder.AddTimeout(TimeSpan.FromSeconds(10));
});
```

`AddResiliencePipeline<TOptions>` is the DI overload added alongside dynamic reload — it resolves
`IOptionsMonitor<TOptions>` and re-reads `CurrentValue` on every rebuild, so **a configuration
change rebuilds the pipeline**. See [dynamic-reload.md](dynamic-reload.md) for the resolve-per-call
contract that makes that work.

To bind two sections of the same DTO type, use named options and the `optionsName` overload:

```csharp
services.Configure<RetryStrategyConfiguration>("api",    config.GetSection("Resilience:Api:Retry"));
services.Configure<RetryStrategyConfiguration>("billing", config.GetSection("Resilience:Billing:Retry"));

services.AddResiliencePipeline<RetryStrategyConfiguration>("api",     "api",     (b, c) => b.AddRetry(c.ToOptions()));
services.AddResiliencePipeline<RetryStrategyConfiguration>("billing", "billing", (b, c) => b.AddRetry(c.ToOptions()));
```

## Delegates are not bindable, and are layered afterwards

`ShouldHandle`, `OnRetry`, `FallbackAction`, `RateLimiter` and the generators are absent from the
DTOs. A delegate cannot come from a configuration file, and a property that silently does nothing
when bound is worse than no property.

Layer them on with a record `with` expression. This needs no extra API, and it is the intended
pattern rather than a workaround:

```csharp
services.AddResiliencePipeline<RetryStrategyConfiguration>("api", (b, c) =>
    b.AddRetry(c.ToOptions() with
    {
        ShouldHandle = ex => ex is HttpRequestException,
        OnRetry = new Action<RetryAttemptEvent>(e =>
            logger.LogWarning("Retry {Attempt} after {Delay}", e.AttemptNumber, e.RetryDelay)),
    }));
```

> The `new Action<RetryAttemptEvent>(...)` wrapper is required — a bare lambda does not compile
> against `ResilienceEventHandler<TArgs>`. See [troubleshooting.md](troubleshooting.md).

`ToOptions()` leaves every delegate `null`, so each strategy's own default predicate applies
untouched when you set none.

## The backoff shape

`RetryDelayConfiguration.Kind` selects the variant explicitly:

| `Kind` | Produces | Meaning |
|---|---|---|
| `Constant` | `RetryDelay.Constant(BaseDelay)` | the same delay between every attempt |
| `Linear` | `RetryDelay.Linear(BaseDelay)` | grows linearly with the attempt number |
| `Exponential` *(default)* | `RetryDelay.Exponential(BaseDelay)` | doubles with each attempt |

**There is no `Custom` member.** `RetryDelay.Custom` takes a delegate, so offering it here would be
the same silently-does-nothing trap as a bindable `ShouldHandle`. `RetryDelayKind_HasNoCustomMember`
pins that.

**The cap lives on the strategy, not the delay.** `RetryStrategyConfiguration.MaxDelay` applies
after the backoff computes its value, for every kind — including `Constant`, whose factory takes no
maximum at all. Exposing a cap on `RetryDelayConfiguration` as well would be two knobs for one
thing, which is the ambiguity this library exists not to have.

```json
{ "Delay": { "Kind": "Constant", "BaseDelay": "00:01:00" }, "MaxDelay": "00:00:10" }
```

That is well-defined: a one-minute constant delay, capped at ten seconds.

## Binding notes

- **`TimeSpan` binds from a string** in `"[d.]hh:mm:ss"` form — `"00:00:05"`, `"00:01:30"`,
  `"1.00:00:00"`. A bare number does not bind.
- **Enums bind from their member name**, case-insensitively: `"Kind": "exponential"` works.
- **Validation happens at `Add*` time, not at bind time.** `ToOptions()` returns the record, and
  the record's own `Validate()` runs when the strategy is added — so a `MaxRetryAttempts` of `-1`
  throws when the pipeline is built, naming the property, rather than producing a quietly wrong
  pipeline. The one thing the DTO validates itself is an out-of-range `RetryDelayKind`.
- **Reflection binding is a trim and AOT hazard.** `Resilion.Extensions` is already
  `IsAotCompatible=false`, but if you publish trimmed, use the source-generated configuration
  binder (`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`), which
  these DTOs are shaped for: plain POCOs, parameterless constructors, settable properties.
- **No new package dependency.** The DTOs are plain classes; your own
  `Microsoft.Extensions.Options.ConfigurationExtensions` does the binding.

## What is not supported, and why

**Whole pipelines from JSON** — a strategy array with type discriminators and ordering read from
configuration — is deliberately out of scope.

It sounds like the natural next step and it is a trap:

- `OrderingValidator`'s guarantees would become a property of a JSON file. A pipeline whose
  circuit breaker ends up outside its retry still *builds*; it just stops doing what the operator
  thought it did. Today that misordering is caught against a literal you can read in one place.
- It does not remove the code. Every predicate, every callback, every rate limiter instance still
  has to be written in C# and attached by name, so you end up maintaining a pipeline in two files
  instead of one, with the configuration half unable to express half of it.
- The failure diagnostics get strictly worse: a typo in a strategy name is a runtime lookup
  failure at first use, where today it is a compiler error.

What you can configure is each strategy's *values*, which is where the operational knobs actually
are — timeouts, attempt counts, thresholds, durations. The pipeline's *shape* stays in code.

## See also

- [retry.md](retry.md) — the retry strategy and `RetryDelay` in full
- [dynamic-reload.md](dynamic-reload.md) — rebuilding a pipeline when configuration changes
- [comparison-with-polly.md](comparison-with-polly.md) — why `RetryDelay` is a closed union
