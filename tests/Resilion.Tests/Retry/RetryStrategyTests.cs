using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

public class RetryStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Happy path — no retries needed
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_Success_NoRetries()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry());

        var result = await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            return new ValueTask<string>("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Sync_Success_NoRetries()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry());

        var result = pipeline.Execute(ct =>
        {
            callCount++;
            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // Retries on exception
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_RetriesOnException_ThenSucceeds()
    {
        var callCount = 0;
        var fakeTime = new FakeTimeProvider();

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = RetryDelay.None, // No delay for test speed
            });
        });

        var result = await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new InvalidOperationException("transient");
            }

            return new ValueTask<string>("recovered");
        });

        Assert.Equal("recovered", result);
        Assert.Equal(3, callCount); // 1 initial + 2 retries before success
    }

    [Fact]
    public async Task Async_ExhaustsRetries_ThrowsLastException()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new InvalidOperationException($"attempt-{callCount}");
            }).AsTask());

        Assert.Equal(3, callCount); // 1 initial + 2 retries
        Assert.Equal("attempt-3", ex.Message); // Last exception propagates
    }

    // ──────────────────────────────────────────────────────────────────
    // MaxRetryAttempts = 0 — no retries
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_ZeroRetries_ExecutesOnce()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 0,
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new InvalidOperationException("fail");
            }).AsTask());

        Assert.Equal(1, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // ShouldHandle predicate
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_UnhandledException_DoesNotRetry()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = RetryDelay.None,
            ShouldHandle = ex => ex is HttpRequestException, // Only retry HttpRequestException
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new InvalidOperationException("not retryable");
            }).AsTask());

        Assert.Equal(1, callCount); // Did NOT retry
    }

    [Fact]
    public async Task Async_OperationCanceledException_NeverRetriedByDefault()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = RetryDelay.None,
        }));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new OperationCanceledException();
            }).AsTask());

        Assert.Equal(1, callCount); // Did NOT retry OCE
    }

    // ──────────────────────────────────────────────────────────────────
    // OnRetry callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OnRetry_FiredForEachRetry()
    {
        var retryEvents = new List<RetryAttemptEvent>();
        Action<RetryAttemptEvent> onRetry = e => retryEvents.Add(e);

        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = RetryDelay.None,
            OnRetry = onRetry,
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new InvalidOperationException($"fail-{callCount}");
            }).AsTask());

        Assert.Equal(3, retryEvents.Count);
        Assert.Equal(1, retryEvents[0].AttemptNumber);
        Assert.Equal(2, retryEvents[1].AttemptNumber);
        Assert.Equal(3, retryEvents[2].AttemptNumber);
    }

    // ──────────────────────────────────────────────────────────────────
    // Cancellation before retry
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_PreCancelledToken_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var callCount = 0;

        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = RetryDelay.None,
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                callCount++;
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("fail");
            }, cts.Token).AsTask());

        // With a pre-cancelled token, the retry loop checks cancellation before each attempt.
        Assert.True(callCount <= 1);
    }

    // ──────────────────────────────────────────────────────────────────
    // RetryDelay variants
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ConstantDelay_ReturnsSameValue()
    {
        var delay = RetryDelay.Constant(TimeSpan.FromMilliseconds(100));

        var d1 = delay.ComputeDelay(1, useJitter: false);
        var d2 = delay.ComputeDelay(2, useJitter: false);
        var d3 = delay.ComputeDelay(3, useJitter: false);

        Assert.Equal(TimeSpan.FromMilliseconds(100), d1);
        Assert.Equal(TimeSpan.FromMilliseconds(100), d2);
        Assert.Equal(TimeSpan.FromMilliseconds(100), d3);
    }

    [Fact]
    public void LinearDelay_ScalesLinearly()
    {
        var delay = RetryDelay.Linear(TimeSpan.FromSeconds(1));

        var d1 = delay.ComputeDelay(1, useJitter: false);
        var d2 = delay.ComputeDelay(2, useJitter: false);
        var d3 = delay.ComputeDelay(3, useJitter: false);

        Assert.Equal(TimeSpan.FromSeconds(1), d1);
        Assert.Equal(TimeSpan.FromSeconds(2), d2);
        Assert.Equal(TimeSpan.FromSeconds(3), d3);
    }

    [Fact]
    public void ExponentialDelay_ScalesExponentially()
    {
        var delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(60));

        var d1 = delay.ComputeDelay(1, useJitter: false);
        var d2 = delay.ComputeDelay(2, useJitter: false);
        var d3 = delay.ComputeDelay(3, useJitter: false);
        var d4 = delay.ComputeDelay(4, useJitter: false);

        Assert.Equal(TimeSpan.FromSeconds(1), d1);  // 1 * 2^0 = 1
        Assert.Equal(TimeSpan.FromSeconds(2), d2);  // 1 * 2^1 = 2
        Assert.Equal(TimeSpan.FromSeconds(4), d3);  // 1 * 2^2 = 4
        Assert.Equal(TimeSpan.FromSeconds(8), d4);  // 1 * 2^3 = 8
    }

    [Fact]
    public void ExponentialDelay_ClampedByMaxDelay()
    {
        var delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(5));

        var d4 = delay.ComputeDelay(4, useJitter: false); // 8s clamped to 5s

        Assert.Equal(TimeSpan.FromSeconds(5), d4);
    }

    [Fact]
    public void Jitter_ProducesVariation()
    {
        var delay = RetryDelay.Constant(TimeSpan.FromSeconds(1));

        // With jitter, values should vary. Run multiple times.
        var delays = Enumerable.Range(1, 100)
            .Select(_ => delay.ComputeDelay(1, useJitter: true))
            .ToList();

        // All within ±25% of 1 second (750ms to 1250ms)
        Assert.All(delays, d =>
        {
            Assert.InRange(d.TotalMilliseconds, 750, 1250);
        });

        // Not all the same (jitter should produce variation)
        Assert.True(delays.Distinct().Count() > 1, "Jitter should produce variation");
    }

    [Fact]
    public void CustomDelay_UsesProvidedFunction()
    {
        var delay = RetryDelay.Custom(ctx => TimeSpan.FromSeconds(ctx.AttemptNumber * 5));

        Assert.Equal(TimeSpan.FromSeconds(5), delay.ComputeDelay(1, useJitter: false));
        Assert.Equal(TimeSpan.FromSeconds(10), delay.ComputeDelay(2, useJitter: false));
    }

    [Fact]
    public void NoneDelay_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, RetryDelay.None.ComputeDelay(1, useJitter: false));
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline — result-based retry
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypedPipeline_RetriesOnBadResult()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<int>(b => b.AddRetry(new RetryStrategyOptions<int>
        {
            MaxRetryAttempts = 3,
            Delay = RetryDelay.None,
            ShouldHandle = outcome =>
                outcome.TryGetResult(out var val) && val < 0, // Retry negative results
        }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            return new ValueTask<int>(callCount < 3 ? -1 : 42);
        });

        Assert.Equal(42, result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task TypedPipeline_ReturnsBadResult_WhenRetriesExhausted()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddRetry(new RetryStrategyOptions<int>
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
            ShouldHandle = outcome =>
                outcome.TryGetResult(out var val) && val == -1,
        }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));

        // After exhausting retries, the last result is returned (not an exception)
        Assert.Equal(-1, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NegativeMaxRetryAttempts_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = -1 })));
    }

    // ──────────────────────────────────────────────────────────────────
    // Composition with Timeout (sync path, avoids FakeTimeProvider coordination issues)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryWithTimeout_RetriesTimeoutRejectedException()
    {
        var callCount = 0;

        // Retry wrapping a pipeline that throws TimeoutRejectedException
        var pipeline = Pipeline.Create(b =>
        {
            b.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = RetryDelay.None,
                ShouldHandle = ex => ex is TimeoutRejectedException,
            });
        });

        var result = await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            if (callCount <= 2)
            {
                throw new TimeoutRejectedException(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1.1),
                    new OperationCanceledException());
            }

            return new ValueTask<string>("success");
        });

        Assert.Equal("success", result);
        Assert.Equal(3, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // MaxDelay — global safety cap
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MaxDelay_CapsComputedDelayFromCustomDelegate()
    {
        var fakeTime = new FakeTimeProvider();
        TimeSpan? capturedDelay = null;
        Action<RetryAttemptEvent> onRetry = e => capturedDelay = e.RetryDelay;

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.Custom(ctx => TimeSpan.FromHours(1)), // absurdly large
                MaxDelay = TimeSpan.FromSeconds(5),
                UseJitter = false,
                OnRetry = onRetry,
            });
        });

        var callCount = 0;
        var executeTask = pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new InvalidOperationException("fail")
                : new ValueTask<string>("ok");
        }).AsTask();

        // Let the strategy reach its (capped) delay wait, then advance the fake clock past it.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        var result = await executeTask;

        Assert.Equal("ok", result);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedDelay);
    }

    [Fact]
    public void NegativeMaxDelay_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
            {
                MaxDelay = TimeSpan.FromSeconds(-1),
            })));
    }
}

// Needed for the ShouldHandle test
file class HttpRequestException : Exception
{
    public HttpRequestException(string message) : base(message) { }
}
