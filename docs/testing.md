# Testing

How to test code that uses Resilion.

## Replace with empty pipeline

Simplest approach — bypass resilience for unit tests:

```csharp
// Production
var pipeline = Pipeline.Create(b => b.AddRetry().AddTimeout(TimeSpan.FromSeconds(10)));

// Test
var pipeline = Pipeline.Empty;  // No strategies, direct passthrough
```

## TimeProvider for deterministic tests

All time-dependent strategies (Timeout, Retry delays, Circuit Breaker recovery) accept `TimeProvider` through the builder. Use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="9.0.0" />
```

```csharp
using Microsoft.Extensions.Time.Testing;

[Fact]
public async Task Timeout_Fires_After_Configured_Duration()
{
    var fakeTime = new FakeTimeProvider();

    var pipeline = Pipeline.Create(b =>
    {
        b.TimeProvider = fakeTime;
        b.AddTimeout(TimeSpan.FromSeconds(5));
    });

    var task = pipeline.ExecuteAsync(async ct =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
        return "late";
    });

    fakeTime.Advance(TimeSpan.FromSeconds(6));

    await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());
}
```

## Testing retry behavior

```csharp
[Fact]
public async Task Retries_Three_Times_Then_Succeeds()
{
    var callCount = 0;
    var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.None,  // No delays in tests
    }));

    var result = await pipeline.ExecuteAsync(ct =>
    {
        callCount++;
        if (callCount < 3)
            throw new InvalidOperationException("transient");
        return new ValueTask<string>("recovered");
    });

    Assert.Equal("recovered", result);
    Assert.Equal(3, callCount);
}
```

## Testing circuit breaker state

Trip the circuit and verify rejection:

```csharp
[Fact]
public async Task Circuit_Trips_After_Failures()
{
    var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatioThreshold = 0.5,
        MinimumThroughput = 4,
        BreakDuration = TimeSpan.FromSeconds(30),
    }));

    // 4 failures = 100% failure rate, above 50% threshold
    for (var i = 0; i < 4; i++)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<int>(ct =>
                throw new InvalidOperationException("fail")).AsTask());
    }

    // Next call rejected
    await Assert.ThrowsAsync<CircuitBrokenException>(() =>
        pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());
}
```

## Testing with manual circuit control

```csharp
[Fact]
public async Task Manual_Isolation_Rejects_All()
{
    var control = new CircuitBreakerManualControl();
    var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        ManualControl = control,
    }));

    await control.IsolateAsync();

    await Assert.ThrowsAsync<CircuitBrokenException>(() =>
        pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());
}
```

## Testing fallback

```csharp
[Fact]
public async Task Fallback_Returns_Default_On_Failure()
{
    var pipeline = Pipeline.Create<string>(b => b.AddFallback(
        new FallbackStrategyOptions<string> { FallbackAction = "default" }));

    var result = await pipeline.ExecuteAsync(ct =>
    {
        throw new Exception("fail");
        return new ValueTask<string>("unreachable");
    });

    Assert.Equal("default", result);
}
```

## Testing callbacks

Capture event args to verify strategy behavior:

```csharp
[Fact]
public async Task OnRetry_Receives_Correct_Attempt_Numbers()
{
    var attempts = new List<int>();
    Action<RetryAttemptEvent> onRetry = e => attempts.Add(e.AttemptNumber);

    var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.None,
        OnRetry = onRetry,
    }));

    await Assert.ThrowsAsync<Exception>(() =>
        pipeline.ExecuteAsync<int>(ct => throw new Exception("fail")).AsTask());

    Assert.Equal([1, 2, 3], attempts);
}
```

## Tips

- Use `RetryDelay.None` in tests to avoid real delays
- Use `FakeTimeProvider` for timeout/circuit breaker recovery testing
- Use `Pipeline.Empty` when resilience behavior is not under test
- Use callbacks (`OnRetry`, `OnTimeout`, etc.) to verify strategy activation without inspecting internals
