using Resilion;

Console.WriteLine("Resilion Samples");
Console.WriteLine("================");
Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 1. Basic retry pipeline
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("1. Basic Retry");
Console.WriteLine("──────────────");

Action<RetryAttemptEvent> onRetry = e =>
    Console.WriteLine($"   Retry #{e.AttemptNumber}, waiting {e.RetryDelay.TotalMilliseconds:F0}ms");

var retryPipeline = Pipeline.Create(b => b
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100)),
        UseJitter = true,
        OnRetry = onRetry,
    }));

var attemptCount = 0;
var result = await retryPipeline.ExecuteAsync(ct =>
{
    attemptCount++;
    if (attemptCount < 3)
    {
        throw new HttpRequestException($"Transient failure (attempt {attemptCount})");
    }

    return new ValueTask<string>("Success!");
});

Console.WriteLine($"   Result: {result} (after {attemptCount} attempts)");
Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 2. Timeout
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("2. Timeout");
Console.WriteLine("──────────");

Action<OnTimeoutArgs> onTimeout = e =>
    Console.WriteLine($"   Timed out after {e.ElapsedTime.TotalMilliseconds:F0}ms");

var timeoutPipeline = Pipeline.Create(b => b
    .AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromMilliseconds(500),
        OnTimeout = onTimeout,
    }));

try
{
    await timeoutPipeline.ExecuteAsync(async ct =>
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return "should not reach";
    });
}
catch (TimeoutRejectedException ex)
{
    Console.WriteLine($"   Caught: {ex.GetType().Name} ({ex.ConfiguredTimeout.TotalMilliseconds}ms timeout)");
}

Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 3. Circuit Breaker
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("3. Circuit Breaker");
Console.WriteLine("──────────────────");

Action<CircuitStateChangedEvent> onOpened = e =>
    Console.WriteLine($"   Circuit OPENED (was {e.PreviousState})");
Action<CircuitStateChangedEvent> onClosed = e =>
    Console.WriteLine($"   Circuit CLOSED (was {e.PreviousState})");

var cbPipeline = Pipeline.Create(b => b
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatioThreshold = 0.5,
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(1),
        OnOpened = onOpened,
        OnClosed = onClosed,
    }));

for (var i = 0; i < 5; i++)
{
    try
    {
        await cbPipeline.ExecuteAsync<string>(ct =>
            throw new InvalidOperationException("Service down"));
    }
    catch (CircuitBrokenException)
    {
        Console.WriteLine($"   Call {i + 1}: Rejected (circuit open)");
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine($"   Call {i + 1}: Failed (circuit tracking)");
    }
}

Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 4. Fallback with typed pipeline
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("4. Fallback");
Console.WriteLine("───────────");

Action<OnFallbackEvent<string>> onFallback = e =>
    Console.WriteLine($"   Fallback activated: {e.Outcome.Exception?.GetType().Name}");

var fallbackPipeline = Pipeline.Create<string>(b => b
    .AddFallback(new FallbackStrategyOptions<string>
    {
        FallbackAction = "default-response",
        OnFallback = onFallback,
    }));

var fallbackResult = await fallbackPipeline.ExecuteAsync(ct =>
{
    throw new InvalidOperationException("Primary failed");
#pragma warning disable CS0162
    return new ValueTask<string>("unreachable");
#pragma warning restore CS0162
});
Console.WriteLine($"   Result: {fallbackResult}");
Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 5. Composite pipeline (the real-world pattern)
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("5. Composite Pipeline");
Console.WriteLine("─────────────────────");

var compositePipeline = Pipeline.Create(b => b
    .AddTimeout(TimeSpan.FromSeconds(30))           // Total timeout
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(200)),
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatioThreshold = 0.5,
        MinimumThroughput = 10,
    })
    .AddTimeout(TimeSpan.FromSeconds(5)));          // Per-attempt timeout

var compositeResult = await compositePipeline.ExecuteAsync(
    static (state, ct) => new ValueTask<string>($"Hello from {state}!"),
    "composite pipeline");

Console.WriteLine($"   Result: {compositeResult}");
Console.WriteLine();

// ──────────────────────────────────────────────────────────────────
// 6. Sync execution
// ──────────────────────────────────────────────────────────────────

Console.WriteLine("6. Sync Execution");
Console.WriteLine("─────────────────");

var syncPipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 2,
    Delay = RetryDelay.None,
}));

var syncAttempts = 0;
var syncResult = syncPipeline.Execute(ct =>
{
    syncAttempts++;
    if (syncAttempts < 2)
    {
        throw new InvalidOperationException("transient");
    }

    return "sync-success";
});

Console.WriteLine($"   Result: {syncResult} (after {syncAttempts} attempts)");
Console.WriteLine();

Console.WriteLine("All samples completed.");
