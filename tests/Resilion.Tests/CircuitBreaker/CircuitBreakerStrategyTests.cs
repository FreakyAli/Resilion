using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

public class CircuitBreakerStrategyTests
{
    private static CircuitBreakerStrategyOptions LowThresholdOptions(
        FakeTimeProvider? fakeTime = null) =>
        new()
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(5),
        };

    // ──────────────────────────────────────────────────────────────────
    // Closed state — happy path
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Closed_SuccessfulCalls_StayClosed()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        for (var i = 0; i < 20; i++)
        {
            var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
            Assert.Equal(42, result);
        }
    }

    [Fact]
    public void Sync_Closed_SuccessfulCalls_StayClosed()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        for (var i = 0; i < 20; i++)
        {
            var result = pipeline.Execute(ct => 42);
            Assert.Equal(42, result);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Closed → Open transition
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Closed_ExceedsFailureRatio_TripsToOpen()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        // Generate enough failures to trip: 4 failures out of 4 calls = 100% > 50%
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("fail")).AsTask());
        }

        // Next call should be rejected
        var ex = await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync<int>(ct => new ValueTask<int>(42)).AsTask());

        Assert.Equal(CircuitState.Open, ex.CircuitState);
        Assert.True(ex.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Closed_BelowMinimumThroughput_DoesNotTrip()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        // 3 failures — below MinimumThroughput of 4
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("fail")).AsTask());
        }

        // Should still be closed — not enough throughput to evaluate ratio
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Closed_BelowFailureRatio_DoesNotTrip()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        // 1 failure + 3 successes = 25% < 50% threshold
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<int>(ct => throw new InvalidOperationException("fail")).AsTask());

        for (var i = 0; i < 3; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        }

        // Should still be closed
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(99));
        Assert.Equal(99, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Open → HalfOpen → Closed recovery
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_AfterBreakDuration_TransitionsToHalfOpen_ThenClosed()
    {
        var fakeTime = new FakeTimeProvider();
        var stateChanges = new List<(CircuitState From, CircuitState To)>();
        Action<CircuitStateChangedEvent> onState = e =>
            stateChanges.Add((e.PreviousState, e.CurrentState));

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(5),
                OnOpened = onState,
                OnHalfOpened = onState,
                OnClosed = onState,
            });
        });

        // Trip the circuit: 2 failures
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("fail")).AsTask());
        }

        // Should be open
        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync<int>(ct => new ValueTask<int>(1)).AsTask());

        // Advance past break duration
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Next call should succeed (probe in half-open)
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);

        // State transitions should include: Closed→Open, Open→HalfOpen, HalfOpen→Closed
        Assert.Contains(stateChanges, s => s.From == CircuitState.Closed && s.To == CircuitState.Open);
        Assert.Contains(stateChanges, s => s.From == CircuitState.Open && s.To == CircuitState.HalfOpen);
        Assert.Contains(stateChanges, s => s.From == CircuitState.HalfOpen && s.To == CircuitState.Closed);
    }

    // ──────────────────────────────────────────────────────────────────
    // HalfOpen — probe fails → back to Open
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HalfOpen_ProbeFails_TransitionsBackToOpen()
    {
        var fakeTime = new FakeTimeProvider();

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5),
            });
        });

        // Trip the circuit
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("fail")).AsTask());
        }

        // Advance past break duration
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Probe fails
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<int>(ct =>
                throw new InvalidOperationException("still failing")).AsTask());

        // Should be Open again — next call is rejected
        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync<int>(ct => new ValueTask<int>(1)).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // OperationCanceledException — not counted as failure
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OCE_NotCountedAsFailure()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 2,
        }));

        // 2 cancellations should NOT trip the circuit
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new OperationCanceledException()).AsTask());
        }

        // Circuit should still be closed
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Manual control
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManualControl_Isolate_RejectsAllCalls()
    {
        var control = new CircuitBreakerManualControl();
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ManualControl = control,
        }));

        // Should work before isolation
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));
        Assert.Equal(1, result);

        // Isolate
        await control.IsolateAsync();

        // Should be rejected
        var ex = await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync<int>(ct => new ValueTask<int>(1)).AsTask());
        Assert.Equal(CircuitState.Isolated, ex.CircuitState);
    }

    [Fact]
    public async Task ManualControl_Reset_AllowsCalls()
    {
        var control = new CircuitBreakerManualControl();
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ManualControl = control,
        }));

        await control.IsolateAsync();
        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync<int>(ct => new ValueTask<int>(1)).AsTask());

        await control.ResetAsync();

        // Should work again
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ManualControl_WithoutStrategy_ThrowsInvalidOperation()
    {
        var control = new CircuitBreakerManualControl();

        await Assert.ThrowsAsync<InvalidOperationException>(() => control.IsolateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => control.ResetAsync());
    }

    // ──────────────────────────────────────────────────────────────────
    // Custom ShouldHandle predicate
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldHandle_OnlyCountsMatchingExceptions()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 2,
            ShouldHandle = ex => ex is TimeoutException, // Only count TimeoutException
        }));

        // InvalidOperationException should NOT count as failure
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("not counted")).AsTask());
        }

        // Circuit should still be closed
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidFailureRatio_ThrowsAtBuildTime(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = ratio,
            })));
    }

    [Fact]
    public void ZeroMinimumThroughput_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                MinimumThroughput = 0,
            })));
    }

    // ──────────────────────────────────────────────────────────────────
    // Concurrent access (basic thread safety)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAccess_NoExceptionsOrCorruption()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.8,
            MinimumThroughput = 100,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(1),
        }));

        var successCount = 0;
        var failureCount = 0;
        var rejectionCount = 0;

        var tasks = Enumerable.Range(0, 500).Select(async i =>
        {
            try
            {
                await pipeline.ExecuteAsync(ct =>
                {
                    if (i % 5 == 0) // 20% failure rate — below 80% threshold
                    {
                        throw new InvalidOperationException("fail");
                    }

                    return new ValueTask<int>(i);
                });

                Interlocked.Increment(ref successCount);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref failureCount);
            }
            catch (CircuitBrokenException)
            {
                Interlocked.Increment(ref rejectionCount);
            }
        });

        await Task.WhenAll(tasks);

        // With 20% failure rate and 80% threshold, the circuit should NOT trip.
        Assert.Equal(0, rejectionCount);
        Assert.True(successCount > 0);
        Assert.True(failureCount > 0);
        Assert.Equal(500, successCount + failureCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline — result-based circuit breaker
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypedPipeline_TripsOnBadResults()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddCircuitBreaker(
            new CircuitBreakerStrategyOptions<int>
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = outcome =>
                    outcome.TryGetResult(out var val) && val == -1, // -1 is a "failure"
            }));

        // 2 "bad" results should trip
        await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));

        // Should be tripped
        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(42)).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // Exception propagation — exceptions still propagate even when counted
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exception_StillPropagates_WhenCountedAsFailure()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(LowThresholdOptions()));

        // The exception should propagate, AND be counted as a failure
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<int>(ct =>
                throw new InvalidOperationException("should propagate")).AsTask());

        Assert.Equal("should propagate", ex.Message);
    }
}
