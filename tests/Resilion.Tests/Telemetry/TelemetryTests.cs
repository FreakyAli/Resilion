using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using Resilion.RateLimiting;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// Verifies each strategy actually emits its documented telemetry counter, and that the two
/// dead instruments removed in the correctness-fix phase (<c>resilion.strategy.executions</c>,
/// <c>resilion.strategy.duration</c>) no longer exist on the "Resilion" meter.
/// </summary>
public class TelemetryTests
{
    /// <summary>Tracks measurements recorded for a single named instrument on the "Resilion" meter.</summary>
    private sealed class CounterTracker : IDisposable
    {
        private readonly MeterListener _listener;
        private long _count;

        public CounterTracker(string instrumentName)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == ResilionTelemetry.MeterName && instrument.Name == instrumentName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                Interlocked.Add(ref _count, measurement));
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task RetryAttempts_IncrementsOnEachRetry()
    {
        using var tracker = new CounterTracker("resilion.retry.attempts");

        var callCount = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
        }));

        await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new InvalidOperationException("fail")
                : new ValueTask<string>("ok");
        });

        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public async Task CircuitBreakerStateChanges_IncrementsOnTrip()
    {
        using var tracker = new CounterTracker("resilion.circuit_breaker.state_changes");

        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 2,
        }));

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync<int>(ct => throw new InvalidOperationException("fail")).AsTask());
        }

        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public async Task TimeoutExpirations_IncrementsWhenTimeoutFires()
    {
        using var tracker = new CounterTracker("resilion.timeout.expirations");

        var fakeTime = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(TimeSpan.FromSeconds(5));
        });

        var executeTask = pipeline.ExecuteAsync(async (ct) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return "unreachable";
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => executeTask);

        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public async Task FallbackActivations_IncrementsWhenFallbackTriggers()
    {
        using var tracker = new CounterTracker("resilion.fallback.activations");

        var pipeline = Pipeline.Create<int>(b => b.AddFallback(new FallbackStrategyOptions<int>
        {
            FallbackAction = -1,
        }));

        var result = await pipeline.ExecuteAsync(ct => throw new InvalidOperationException("fail"));

        Assert.Equal(-1, result);
        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public async Task HedgingAttempts_IncrementsWhenHedgeLaunches()
    {
        using var tracker = new CounterTracker("resilion.hedging.attempts");

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(new HedgingStrategyOptions<string>
        {
            MaxHedgedAttempts = 2,
            HedgingDelay = TimeSpan.Zero, // Parallel mode — hedge always launches.
        }));

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));

        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public async Task RateLimiterRejections_IncrementsWhenLimitExceeded()
    {
        using var tracker = new CounterTracker("resilion.rate_limiter.rejections");

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
        try
        {
            await Assert.ThrowsAsync<RateLimitRejectedException>(() =>
                pipeline.ExecuteAsync(ct => new ValueTask<int>(1)).AsTask());
        }
        finally
        {
            lease.Dispose();
        }

        Assert.True(tracker.Count >= 1);
    }

    [Fact]
    public void DeadInstruments_NoLongerExistOnTheMeter()
    {
        var observedNames = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ResilionTelemetry.MeterName)
                {
                    observedNames.Add(instrument.Name);
                }
            },
        };
        listener.Start();

        Assert.DoesNotContain("resilion.strategy.executions", observedNames);
        Assert.DoesNotContain("resilion.strategy.duration", observedNames);
    }
}
