using System.Threading.RateLimiting;
using Resilion.RateLimiting;
using Xunit;

namespace Resilion.Tests;

public class RateLimiterStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Happy path — within limit
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_WithinLimit_ExecutesNormally()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public void Sync_WithinLimit_ExecutesNormally()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var result = pipeline.Execute(ct => "ok");
        Assert.Equal("ok", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Exceeds limit — rejected
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_ExceedsLimit_ThrowsRateLimitRejectedException()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        // Acquire the only permit manually so pipeline execution is rejected.
        var lease = limiter.AttemptAcquire();
        Assert.True(lease.IsAcquired);

        try
        {
            await Assert.ThrowsAsync<RateLimitRejectedException>(() =>
                pipeline.ExecuteAsync(ct => new ValueTask<string>("should not reach")).AsTask());
        }
        finally
        {
            lease.Dispose();
        }
    }

    [Fact]
    public void Sync_ExceedsLimit_ThrowsRateLimitRejectedException()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var lease = limiter.AttemptAcquire();
        Assert.True(lease.IsAcquired);

        try
        {
            Assert.Throws<RateLimitRejectedException>(() =>
                pipeline.Execute(ct => "should not reach"));
        }
        finally
        {
            lease.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Lease is released after execution (success and failure)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_LeaseReleasedAfterSuccess()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        // First call should succeed and release the lease
        await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));

        // Second call should also succeed (lease was released)
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(2));
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task Async_LeaseReleasedAfterException()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        // First call throws but should release the lease
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<int>(ct => throw new InvalidOperationException("fail")).AsTask());

        // Second call should succeed (lease was released)
        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // OnRejected callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OnRejected_IsFired()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
        });

        OnRateLimitRejectedEvent? capturedEvent = null;
        Action<OnRateLimitRejectedEvent> onRejected = e => capturedEvent = e;

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
            OnRejected = onRejected,
        }));

        var lease = limiter.AttemptAcquire();
        try
        {
            await Assert.ThrowsAsync<RateLimitRejectedException>(() =>
                pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());

            Assert.NotNull(capturedEvent);
        }
        finally
        {
            lease.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Token bucket — RetryAfter metadata
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_TokenBucket_ProvidesRetryAfter()
    {
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            QueueLimit = 0,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 1,
            AutoReplenishment = false,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        // Exhaust the single token
        await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));

        // Next call should be rejected
        var ex = await Assert.ThrowsAsync<RateLimitRejectedException>(() =>
            pipeline.ExecuteAsync(ct => new ValueTask<int>(2)).AsTask());

        // TokenBucketRateLimiter provides RetryAfter metadata
        Assert.NotNull(ex.RetryAfter);
        Assert.True(ex.RetryAfter.Value > TimeSpan.Zero);
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypedPipeline_RateLimiter_Works()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create<string>(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("typed-ok"));
        Assert.Equal("typed-ok", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingRateLimiter_ThrowsAtBuildTime()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions())));
    }

    // ──────────────────────────────────────────────────────────────────
    // Concurrent access
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAccess_ConcurrencyLimiter_EnforcesLimit()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 0,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var concurrentCount = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            try
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    var current = Interlocked.Increment(ref concurrentCount);
                    InterlockedMax(ref maxConcurrent, current);
                    await Task.Delay(50, ct);
                    Interlocked.Decrement(ref concurrentCount);
                    return 0;
                });
            }
            catch (RateLimitRejectedException)
            {
                // Expected — some will be rejected
            }
        });

        await Task.WhenAll(tasks);

        // Concurrency should never exceed the permit limit
        Assert.True(maxConcurrent <= 5, $"Max concurrent was {maxConcurrent}, expected <= 5");
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = location;
            if (value <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    // ──────────────────────────────────────────────────────────────────
    // AttemptAcquire (sync) vs AcquireAsync (async) — queueing behavior differs
    // ──────────────────────────────────────────────────────────────────
    //
    // ExecuteAsync uses AcquireAsync, which will wait in queue up to QueueLimit permits.
    // Execute (sync) uses AttemptAcquire, which never queues — it rejects immediately if no
    // permit is free right now, even when QueueLimit would have let AcquireAsync wait.

    [Fact]
    public async Task Async_WithQueueLimit_WaitsInQueueThenSucceeds()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 1,
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        // Hold the only permit so the next acquire must queue.
        var heldLease = limiter.AttemptAcquire();
        Assert.True(heldLease.IsAcquired);

        var queuedTask = pipeline.ExecuteAsync(ct => new ValueTask<string>("queued-ok")).AsTask();

        // Give the queued acquire a moment to actually start waiting, then free the permit.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        heldLease.Dispose();

        var result = await queuedTask;
        Assert.Equal("queued-ok", result);
    }

    [Fact]
    public void Sync_WithQueueLimit_StillRejectsImmediately()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 1, // Would let an async caller wait — AttemptAcquire ignores this.
        });

        var pipeline = Pipeline.Create(b => b.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = limiter,
        }));

        var heldLease = limiter.AttemptAcquire();
        Assert.True(heldLease.IsAcquired);

        try
        {
            // Sync Execute() uses AttemptAcquire, which never queues, so this rejects
            // immediately instead of waiting for heldLease to be released.
            Assert.Throws<RateLimitRejectedException>(() =>
                pipeline.Execute(ct => "should not reach"));
        }
        finally
        {
            heldLease.Dispose();
        }
    }
}
