# Installation

## The short version

Add one package and you're done:

```xml
<ItemGroup>
    <PackageReference Include="Resilion" Version="0.1.0" />
</ItemGroup>
```

This gives you retry, circuit breaker, timeout, fallback, and hedging — zero external dependencies.

---

## Package decision guide

| Package | When to install |
|---------|----------------|
| `Resilion` | Always — core library with all built-in strategies, zero dependencies |
| `Resilion.RateLimiting` | When you need rate limiting (wraps `System.Threading.RateLimiting`) |
| `Resilion.Extensions` | When you use DI (`IServiceCollection`) for dependency injection |

> **OpenTelemetry metrics**: The `ResilionTelemetry` meter is defined in the core `Resilion` package and can be consumed independently. The `Resilion.Extensions` package provides convenient integration with `IServiceCollection` for DI-based registration.

---

## Dependency graph

```text
Resilion                          (zero dependencies)
  ↑
  ├── Resilion.Extensions         (+ Microsoft.Extensions.*)
  │
  └── Resilion.RateLimiting       (+ System.Threading.RateLimiting)
```

`Resilion.Extensions` and `Resilion.RateLimiting` both reference `Resilion` — NuGet pulls it in transitively.

---

## Quick setup

### Console app / library (no DI)

```csharp
using Resilion;

var pipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10)));

var result = await pipeline.ExecuteAsync(
    static (client, ct) => client.GetStringAsync("https://api.example.com", ct),
    httpClient);
```

### ASP.NET Core / DI

```csharp
using Resilion;
using Resilion.Extensions;

// In Program.cs or Startup:
services.AddResiliencePipeline("http-retry", b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10)));

// In your service:
public class MyService(ResiliencePipelineRegistry<string> registry)
{
    private readonly Pipeline _pipeline = registry.GetPipeline("http-retry");

    public Task<string> GetDataAsync(CancellationToken ct)
        => _pipeline.ExecuteAsync(
            static (state, ct) => state.client.GetStringAsync(state.url, ct),
            (client: _httpClient, url: "https://api.example.com"),
            ct).AsTask();
}
```

### With rate limiting

```xml
<ItemGroup>
    <PackageReference Include="Resilion" Version="0.1.0" />
    <PackageReference Include="Resilion.RateLimiting" Version="0.1.0" />
</ItemGroup>
```

```csharp
using Resilion;
using Resilion.RateLimiting;
using System.Threading.RateLimiting;

var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 10,
    QueueLimit = 0,
});

var pipeline = Pipeline.Create(b => b
    .AddRateLimiter(new RateLimiterStrategyOptions { RateLimiter = limiter })
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 }));
```

---

## Supported .NET versions

- .NET 8.0+

## License

[Apache-2.0](../LICENSE)
