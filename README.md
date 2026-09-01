# Resilion

A modern resilience library for .NET. Retry, circuit breaker, timeout, fallback, rate limiting, hedging, and pipeline composition — with zero external dependencies in the core package.

[![CI](https://github.com/FreakyAli/Resilion/actions/workflows/ci.yml/badge.svg)](https://github.com/FreakyAli/Resilion/actions/workflows/ci.yml)
[![Tests](https://github.com/FreakyAli/Resilion/actions/workflows/test.yml/badge.svg)](https://github.com/FreakyAli/Resilion/actions/workflows/test.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

## Quick Start

```csharp
using Resilion;

var pipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatioThreshold = 0.5,
        MinimumThroughput = 10,
    })
    .AddTimeout(TimeSpan.FromSeconds(5)));

var httpClient = new HttpClient();

var result = await pipeline.ExecuteAsync(
    static (client, ct) => client.GetStringAsync("https://api.example.com/data", ct),
    httpClient);
```

## Why Resilion?

Resilion is designed for experienced .NET developers who want:

- **Minimal API** — one `Pipeline.Create` call, chain strategies, `.Build()`. No framework to learn.
- **Zero-dependency core** — the `Resilion` package has no external dependencies.
- **Sync and async** — both `Execute` and `ExecuteAsync` with true sync implementations (not sync-over-async).
- **Simple predicates** — `Func<Outcome<T>, bool>` instead of Polly's `PredicateBuilder` + `PredicateResult.True` + `ValueTask<bool>`.
- **Simple callbacks** — assign an `Action<T>` directly; no `ValueTask` wrapping for synchronous logging.
- **Composable pipelines** — combine pre-built pipelines with `AddPipeline()`.
- **Allocation-conscious** — `Outcome<T>` is a struct, `ResilienceContext` is pooled, state parameters avoid closures.

## Packages

| Package | Purpose | Dependencies |
|---------|---------|-------------|
| **Resilion** | Core library with all built-in strategies | None |
| **Resilion.Extensions** | DI registration, telemetry (Meter/ActivitySource) | Microsoft.Extensions.* |
| **Resilion.RateLimiting** | Rate limiting strategy | System.Threading.RateLimiting |

## Strategies

### Retry

```csharp
var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),  // or Constant, Linear, Custom
    UseJitter = true,  // Decorrelated jitter (on by default)
}));
```

Result-based retry on typed pipelines:

```csharp
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddRetry(
    new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
        ShouldHandle = outcome =>
            outcome.Exception is HttpRequestException
            || (outcome.TryGetResult(out var r) && (int)r.StatusCode >= 500),
    }));
```

### Timeout

```csharp
var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));
```

Timeout uses cooperative cancellation. The operation must observe the `CancellationToken`.

### Circuit Breaker

```csharp
var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatioThreshold = 0.5,   // Trip at 50% failure rate
    MinimumThroughput = 10,        // Need 10+ calls before evaluating
    SamplingDuration = TimeSpan.FromSeconds(30),
    BreakDuration = TimeSpan.FromSeconds(30),
}));
```

### Fallback

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackAction = "default-value",  // or a Func, or an async Func
    }));
```

### Rate Limiting

```csharp
using Resilion.RateLimiting;

var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 10,
    QueueLimit = 0,
});

var pipeline = Pipeline.Create(b => b.AddRateLimiter(
    new RateLimiterStrategyOptions { RateLimiter = limiter }));
```

### Hedging

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddHedging(
    new HedgingStrategyOptions<string>
    {
        MaxHedgedAttempts = 3,
        HedgingDelay = TimeSpan.FromSeconds(2),  // 0 = parallel, InfiniteTimeSpan = sequential
    }));
```

## Strategy Ordering

Strategies execute outermost to innermost. The canonical order:

```csharp
Pipeline.Create(b => b
    .AddRateLimiter(...)        // 1. Shed load first
    .AddTimeout(30s)            // 2. Total timeout across all retries
    .AddRetry(...)              // 3. Retry failures
    .AddCircuitBreaker(...)     // 4. Track per-attempt success/failure
    .AddTimeout(5s));           // 5. Per-attempt timeout
```

## Dependency Injection

```csharp
using Resilion.Extensions;

services.AddResiliencePipeline("my-pipeline", b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10)));

// Resolve:
var registry = serviceProvider.GetRequiredService<ResiliencePipelineRegistry<string>>();
var pipeline = registry.GetPipeline("my-pipeline");
```

## Supported .NET Versions

- .NET 9.0+

## Project Structure

```text
src/
    Resilion/                   Core library (zero dependencies)
    Resilion.Extensions/        DI, logging, telemetry integration
    Resilion.RateLimiting/      Rate limiting strategy
tests/
    Resilion.Tests/             Core strategy and pipeline tests
    Resilion.Extensions.Tests/  DI and telemetry tests
benchmarks/
    Resilion.Benchmarks/        Performance benchmarks
samples/
    Resilion.Samples/           Usage examples
docs/
    future-plans.md             Roadmap and known tradeoffs
```

## License

[Apache-2.0](LICENSE)
