# Resilion

**A modern resilience library for .NET.** Retry, circuit breaker, timeout, fallback, rate limiting, hedging, and pipeline composition — with zero external dependencies in the core package.

[![CI](https://github.com/FreakyAli/Resilion/actions/workflows/ci.yml/badge.svg)](https://github.com/FreakyAli/Resilion/actions/workflows/ci.yml)
[![Tests](https://github.com/FreakyAli/Resilion/actions/workflows/test.yml/badge.svg)](https://github.com/FreakyAli/Resilion/actions/workflows/test.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Resilion.svg)](https://www.nuget.org/packages/Resilion)

## Quick Start

```csharp
using Resilion;

// Create a resilience pipeline with multiple strategies
var pipeline = Pipeline.Create(b => b
    .AddRateLimiter(...)        // 1. Shed load first
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

// Execute with the pipeline
var result = await pipeline.ExecuteAsync(
    static (httpClient, ct) => httpClient.GetStringAsync("https://api.example.com/data", ct),
    new HttpClient());
```

## Why Resilion?

Resilion is designed for experienced .NET developers who want powerful resilience patterns without complexity:

### Zero Dependencies in Core
The `Resilion` package has **zero external dependencies**. Everything you need for production resilience is built-in.

### Simple, Fluent API
One `Pipeline.Create()` call. Chain strategies. Build. That's it. No framework to learn, no builder patterns, no magic.

```csharp
var pipeline = Pipeline.Create(b => b
    .AddRetry(options)
    .AddCircuitBreaker(options)
    .AddTimeout(duration));
```

### Sync and Async
Both `Execute` and `ExecuteAsync` with **true sync implementations** — not sync-over-async. No blocking calls, no artificial overhead.

```csharp
// Synchronous execution
var result = pipeline.Execute(state => DoWork(state), httpClient);

// Asynchronous execution
var result = await pipeline.ExecuteAsync(async (state, ct) => 
    await DoWorkAsync(state, ct), httpClient);
```

### Outcome-Based Resilience
No `PredicateBuilder` or `PredicateResult` chains. Just `Func<Outcome<T>, bool>`:

```csharp
var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddRetry(
    new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        ShouldHandle = outcome =>
            outcome.Exception is HttpRequestException  // Handle exceptions
            || (outcome.TryGetResult(out var r) && (int)r.StatusCode >= 500),  // or result-based
    }));
```

### Simple Callbacks
Assign callbacks directly. No `ValueTask` wrapping for synchronous logging:

```csharp
new RetryStrategyOptions
{
    OnRetry = (context) => 
    {
        logger.LogWarning($"Retry attempt {context.AttemptNumber}");
    }
}
```

### Composable Pipelines
Combine pre-built pipelines into larger ones:

```csharp
var basePipeline = Pipeline.Create(b => b
    .AddRetry(retryOptions)
    .AddCircuitBreaker(cbOptions));

var fullPipeline = Pipeline.Create(b => b
    .AddRateLimiter(rlOptions)
    .AddPipeline(basePipeline));
```

### Allocation-Conscious Design
- `Outcome<T>` is a struct
- `ResilienceContext` is pooled
- State parameters avoid closures
- Designed for performance-critical paths

## Installation

### Core Package
```bash
dotnet add package Resilion
```

### With Dependency Injection & Telemetry
```bash
dotnet add package Resilion.Extensions
```

### Rate Limiting Strategy
```bash
dotnet add package Resilion.RateLimiting
```

## Packages & Dependencies

| Package | Purpose | Dependencies |
|---------|---------|-------------|
| **Resilion** | Core library with all built-in strategies | None |
| **Resilion.Extensions** | DI registration, telemetry (Meter/ActivitySource), structured logging | Microsoft.Extensions.* |
| **Resilion.RateLimiting** | Rate limiting strategy with multiple algorithms | System.Threading.RateLimiting |

## Strategies

### Retry

Automatically retry failed operations with customizable delay strategies.

```csharp
var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1)),
    UseJitter = true,  // Decorrelated jitter (on by default)
}));
```

**Delay Strategies:**
- `Exponential` — Exponential backoff: 1s, 2s, 4s, 8s, ...
- `Linear` — Linear backoff: 1s, 2s, 3s, 4s, ...
- `Constant` — Fixed delay between retries
- `Custom` — Supply your own delay function

**Result-Based Retry** on typed pipelines:

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

**Use when:**
- Calling unreliable remote services
- Handling transient failures (network glitches, temporary outages)
- Need to respect rate limits with backoff

### Timeout

Enforce operation timeouts with cooperative cancellation.

```csharp
var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));
```

**Key Points:**
- Uses `CancellationToken` — the operation must observe it
- Perfect for async I/O operations
- Can be stacked (total timeout + per-attempt timeout)

**Use when:**
- Preventing indefinite hangs on remote calls
- Enforcing SLA boundaries
- Protecting against slow endpoints

### Circuit Breaker

Prevent cascading failures by stopping requests when failure rates are too high.

```csharp
var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatioThreshold = 0.5,      // Trip at 50% failure rate
    MinimumThroughput = 10,           // Need 10+ calls before evaluating
    SamplingDuration = TimeSpan.FromSeconds(30),
    BreakDuration = TimeSpan.FromSeconds(30),
}));
```

**States:**
- **Closed** — Normal operation, requests pass through
- **Open** — Too many failures, requests rejected immediately
- **Half-Open** — Testing if service has recovered

**Use when:**
- Protecting against cascading failures
- Integrating with dependent services
- Need fast-fail when a service is down

### Fallback

Provide a fallback value or action when operations fail.

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackAction = "default-value",  // Static value
    }));

// Or with a function:
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackAction = (context) => GetCachedValue() ?? "default",
    }));

// Or async:
var pipeline = Pipeline.Create<string>(b => b.AddFallback(
    new FallbackStrategyOptions<string>
    {
        FallbackActionAsync = async (context, ct) => 
            await GetCachedValueAsync(ct) ?? "default",
    }));
```

**Use when:**
- You have a sensible default to return
- Calling a secondary data source
- Providing degraded service instead of failure

### Rate Limiting

Control request throughput to prevent overload.

```csharp
using Resilion.RateLimiting;

var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 10,
    QueueLimit = 0,  // Reject excess requests
});

var pipeline = Pipeline.Create(b => b.AddRateLimiter(
    new RateLimiterStrategyOptions { RateLimiter = limiter }));
```

**Built-in Limiters:**
- `ConcurrencyLimiter` — Limit concurrent operations
- `TokenBucketRateLimiter` — Token bucket algorithm
- `SlidingWindowRateLimiter` — Sliding window rate limiting
- Custom implementations via `System.Threading.RateLimiting`

**Use when:**
- Limiting load on a resource
- Protecting downstream services
- Controlling API consumption

### Hedging

Send duplicate requests if the first one is slow, returning the fastest response.

```csharp
var pipeline = Pipeline.Create<string>(b => b.AddHedging(
    new HedgingStrategyOptions<string>
    {
        MaxHedgedAttempts = 3,
        HedgingDelay = TimeSpan.FromSeconds(2),
    }));
```

**Hedging Delay:**
- `TimeSpan.Zero` — Fire all requests in parallel
- `TimeSpan.FromSeconds(2)` — Wait 2 seconds before sending next request
- `InfiniteTimeSpan` — Sequential requests (no hedging, just fallback)

**Use when:**
- Reducing tail latency in latency-sensitive systems
- You can afford duplicate requests
- Calling idempotent endpoints

## Strategy Ordering (Canonical Pipeline)

Strategies execute **outermost to innermost**. The recommended order is:

```csharp
Pipeline.Create(b => b
    .AddRateLimiter(...)        // 1. Shed load FIRST
    .AddTimeout(30s)            // 2. Total timeout across all retries
    .AddRetry(...)              // 3. Retry failures
    .AddCircuitBreaker(...)     // 4. Track per-attempt success/failure
    .AddTimeout(5s));           // 5. Per-attempt timeout
```

**Why this order?**
1. **Rate Limit** prevents the system from being overwhelmed
2. **Outer Timeout** sets a hard boundary for the entire operation
3. **Retry** gives transient failures a chance to succeed
4. **Circuit Breaker** protects downstream services and fast-fails when they're down
5. **Inner Timeout** per-request prevents individual attempts from hanging

Different use cases may require different orderings — this is the safe default.

## Dependency Injection

Register pipelines with Microsoft.Extensions.DependencyInjection:

```csharp
using Resilion.Extensions;

services.AddResiliencePipeline("http-api", b => b
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10)));

// Later, resolve:
var registry = serviceProvider.GetRequiredService<ResiliencePipelineRegistry<string>>();
var pipeline = registry.GetPipeline("http-api");

var result = await pipeline.ExecuteAsync(
    async (client, ct) => await client.GetStringAsync(url, ct),
    httpClient);
```

## Telemetry

Resilion integrates with `System.Diagnostics`:

```csharp
// Metrics via System.Diagnostics.Metrics.Meter
var meter = new Meter("Resilion");
var retryCount = meter.CreateCounter<long>("resilion.retry.attempt_count");

// Activity tracking via ActivitySource
var activitySource = new ActivitySource("Resilion");
using var activity = activitySource.StartActivity("pipeline.execute");
```

Structured logging is also available through callbacks on strategy options.

## Supported .NET Versions

- **.NET 8.0+**

Resilion is built for modern .NET with full support for:
- Top-level statements
- Records and nullable reference types
- Async/await patterns
- Source generators (future features)

## Project Structure

```
src/
  Resilion/                   Core library (zero dependencies)
  Resilion.Extensions/        DI, telemetry, structured logging
  Resilion.RateLimiting/      Rate limiting strategy
tests/
  Resilion.Tests/             Core strategy and pipeline tests
  Resilion.Extensions.Tests/  DI and telemetry tests
benchmarks/
  Resilion.Benchmarks/        Performance benchmarks (BenchmarkDotNet)
samples/
  Resilion.Samples/           Real-world usage examples
docs/
  *.md                        Feature and architecture documentation
```

## Performance

Resilion is designed for high-performance, latency-sensitive scenarios:

- Struct-based `Outcome<T>` eliminates heap allocations
- Pooled `ResilienceContext` for request-scoped state
- Zero-allocation happy path for most strategies
- Benchmarks included (see `benchmarks/` folder)

Compare with alternatives like Polly to see allocation profiles and throughput characteristics.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on:
- Code style and standards
- Testing requirements
- PR expectations
- Building and running tests locally

## License

[Apache-2.0](LICENSE)

---

**Built for developers who care about reliability.**
