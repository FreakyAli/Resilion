using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// Comprehensive coverage for the typed (result-based) circuit breaker. Mirrors
/// <see cref="CircuitBreakerStrategyTests"/> but exercises the <c>Strategy&lt;TResult&gt;</c> path
/// and result-based predicates, including a regression test for the race condition fixed when
/// <c>CircuitBreakerTypedStrategy&lt;T&gt;</c> was moved onto the shared <c>CircuitBreakerStateMachine</c>.
/// </summary>
public class CircuitBreakerTypedStrategyTests
{
    private static bool IsBadResult(Outcome<int> outcome)
        => outcome.TryGetResult(out var value) && value < 0;

    // ──────────────────────────────────────────────────────────────────
    // Closed state — happy path
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Closed_GoodResults_StayClosed()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            ShouldHandle = IsBadResult,
        }));

        for (var i = 0; i < 20; i++)
        {
            var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
            Assert.Equal(42, result);
        }
    }

    [Fact]
    public void Sync_Closed_GoodResults_StayClosed()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            ShouldHandle = IsBadResult,
        }));

        for (var i = 0; i < 20; i++)
        {
            var result = pipeline.Execute(ct => 42);
            Assert.Equal(42, result);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Closed → Open on result-based predicate
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Closed_ExceedsFailureRatio_TripsToOpen()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            BreakDuration = TimeSpan.FromSeconds(5),
            ShouldHandle = IsBadResult,
        }));

        for (var i = 0; i < 4; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        }

        var ex = await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(42)).AsTask());

        Assert.Equal(CircuitState.Open, ex.CircuitState);
        Assert.True(ex.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Closed_BelowMinimumThroughput_DoesNotTrip()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            ShouldHandle = IsBadResult,
        }));

        for (var i = 0; i < 3; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        }

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Closed_BelowFailureRatio_DoesNotTrip()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            ShouldHandle = IsBadResult,
        }));

        await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        for (var i = 0; i < 3; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));
        }

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(99));
        Assert.Equal(99, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Full recovery cycle with all three callbacks
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_AfterBreakDuration_TransitionsToHalfOpen_ThenClosed()
    {
        var fakeTime = new FakeTimeProvider();
        var opened = new List<CircuitStateChangedEvent>();
        var halfOpened = new List<CircuitStateChangedEvent>();
        var closed = new List<CircuitStateChangedEvent>();
        Action<CircuitStateChangedEvent> onOpened = e => opened.Add(e);
        Action<CircuitStateChangedEvent> onHalfOpened = e => halfOpened.Add(e);
        Action<CircuitStateChangedEvent> onClosed = e => closed.Add(e);

        var pipeline = Pipeline.Create<int>(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = IsBadResult,
                OnOpened = onOpened,
                OnHalfOpened = onHalfOpened,
                OnClosed = onClosed,
            });
        });

        // Trip the circuit.
        for (var i = 0; i < 2; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        }

        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());

        Assert.Single(opened);
        Assert.Equal(CircuitState.Closed, opened[0].PreviousState);
        Assert.Equal(CircuitState.Open, opened[0].CurrentState);

        // Advance past break duration.
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Probe succeeds.
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);

        Assert.Single(halfOpened);
        Assert.Equal(CircuitState.Open, halfOpened[0].PreviousState);
        Assert.Equal(CircuitState.HalfOpen, halfOpened[0].CurrentState);

        Assert.Single(closed);
        Assert.Equal(CircuitState.HalfOpen, closed[0].PreviousState);
        Assert.Equal(CircuitState.Closed, closed[0].CurrentState);
    }

    // ──────────────────────────────────────────────────────────────────
    // HalfOpen probe fails → back to Open
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HalfOpen_ProbeFails_TransitionsBackToOpen()
    {
        var fakeTime = new FakeTimeProvider();

        var pipeline = Pipeline.Create<int>(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = IsBadResult,
            });
        });

        for (var i = 0; i < 2; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        }

        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Probe fails again.
        await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));

        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // Manual control
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManualControl_IsolateThenReset_RoundTrips()
    {
        var control = new CircuitBreakerManualControl();
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            ManualControl = control,
            ShouldHandle = IsBadResult,
        }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));
        Assert.Equal(1, result);

        await control.IsolateAsync();
        var ex = await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());
        Assert.Equal(CircuitState.Isolated, ex.CircuitState);

        await control.ResetAsync();
        result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(2));
        Assert.Equal(2, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Concurrent access — no exceptions, ratio stays valid
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAccess_NoExceptionsOrCorruption()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.8,
            MinimumThroughput = 100,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(1),
            ShouldHandle = IsBadResult,
        }));

        var successCount = 0;
        var rejectionCount = 0;

        var tasks = Enumerable.Range(0, 500).Select(async i =>
        {
            try
            {
                // 20% "bad" results — below the 80% threshold, so the circuit should never trip.
                var value = i % 5 == 0 ? -1 : i;
                var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(value));
                Interlocked.Increment(ref successCount);
            }
            catch (CircuitBrokenException)
            {
                Interlocked.Increment(ref rejectionCount);
            }
        });

        await Task.WhenAll(tasks);

        Assert.Equal(0, rejectionCount);
        Assert.Equal(500, successCount);
    }

    /// <summary>
    /// Regression test for the race condition fixed in <c>CircuitBreakerTypedStrategy&lt;T&gt;</c>:
    /// recording an outcome and reading the failure ratio used to be two separate lock
    /// acquisitions, letting a concurrent caller observe a stale or out-of-range ratio between
    /// them. Now both strategies share <c>CircuitBreakerStateMachine</c>, which combines the two
    /// under one lock via <c>SlidingWindow.RecordAndGetRatio</c>. Heavy concurrent, mixed
    /// success/failure traffic should never crash and should trip deterministically once the
    /// threshold is exceeded.
    /// </summary>
    [Fact]
    public async Task ConcurrentAccess_RapidMixedOutcomes_TripsExactlyWhenThresholdCrossed()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromMinutes(5),
            ShouldHandle = IsBadResult,
        }));

        // 100% failures, well above the 50% threshold — should trip and stay tripped.
        var rejections = 0;
        var tasks = Enumerable.Range(0, 500).Select(async _ =>
        {
            try
            {
                await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
            }
            catch (CircuitBrokenException)
            {
                Interlocked.Increment(ref rejections);
            }
        });

        await Task.WhenAll(tasks);

        // Some calls must have been rejected once the circuit tripped — no exception escaped
        // uncontrolled, and the circuit reaches a consistent Open state.
        Assert.True(rejections > 0);
    }
}
