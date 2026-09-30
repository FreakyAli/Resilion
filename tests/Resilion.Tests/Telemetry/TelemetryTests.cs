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

        /// <param name="instrumentName">The instrument to count.</param>
        /// <param name="pipelineName">
        /// When set, only measurements tagged with this pipeline name are counted.
        /// <see cref="MeterListener"/> is process-global and xunit runs test classes in parallel,
        /// so an unfiltered tracker also counts measurements emitted by other tests. Any test
        /// asserting an exact count must filter.
        /// </param>
        public CounterTracker(string instrumentName, string? pipelineName = null)
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
            {
                if (pipelineName is not null && !HasPipelineName(tags, pipelineName))
                {
                    return;
                }

                Interlocked.Add(ref _count, measurement);
            });
            _listener.Start();
        }

        private static bool HasPipelineName(ReadOnlySpan<KeyValuePair<string, object?>> tags, string expected)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == ResilionTelemetry.PipelineNameTag && (tag.Value as string) == expected)
                {
                    return true;
                }
            }

            return false;
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
        using var tracker = new CounterTracker("resilion.hedging.attempts", "hedge-async-exact");

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.Name = "hedge-async-exact";
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.Zero, // Parallel mode — hedge always launches.
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));

        // Exact, not `>= 1`: the whole defect class here is under-counting, and a `>=` assertion
        // passes with the counter half-broken.
        Assert.Equal(1, tracker.Count);
    }

    // ─── future-plans #55: the sync path never incremented this counter at all ──────────────

    [Fact]
    public void HedgingAttempts_IncrementsOnSyncSequentialHedge()
    {
        using var tracker = new CounterTracker("resilion.hedging.attempts", "hedge-sync");

        var attempt = 0;
        var pipeline = Pipeline.Create<string>(b =>
        {
            b.Name = "hedge-sync";
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                // The only sync-legal mode; parallel and latency modes throw on the sync path.
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                ShouldHandle = o => o.TryGetResult(out var r) && r == "fail",
            });
        });

        var result = pipeline.Execute(ct => ++attempt < 3 ? "fail" : "ok");

        Assert.Equal("ok", result);
        Assert.Equal(3, attempt);

        // Two hedge launches for three attempts: the first attempt is not itself a hedge.
        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public async Task HedgingAttempts_SyncAndAsyncAgreeOnCount()
    {
        // Parity guard. The counter and the two execution paths must not be able to diverge again,
        // and this is also part of the harness that makes the #27 retry-loop dedup safe.
        static HedgingStrategyOptions<string> Options() => new()
        {
            MaxHedgedAttempts = 3,
            HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            ShouldHandle = o => o.TryGetResult(out var r) && r == "fail",
        };

        long syncCount;
        using (var tracker = new CounterTracker("resilion.hedging.attempts", "parity-sync"))
        {
            var attempt = 0;
            var pipeline = Pipeline.Create<string>(b =>
            {
                b.Name = "parity-sync";
                b.AddHedging(Options());
            });
            pipeline.Execute(ct => ++attempt < 3 ? "fail" : "ok");
            syncCount = tracker.Count;
        }

        long asyncCount;
        using (var tracker = new CounterTracker("resilion.hedging.attempts", "parity-async"))
        {
            var attempt = 0;
            var pipeline = Pipeline.Create<string>(b =>
            {
                b.Name = "parity-async";
                b.AddHedging(Options());
            });
            await pipeline.ExecuteAsync(ct => new ValueTask<string>(++attempt < 3 ? "fail" : "ok"));
            asyncCount = tracker.Count;
        }

        Assert.Equal(syncCount, asyncCount);
    }

    // ─── future-plans #59: strategies nested inside hedging emitted pipeline.name = null ────

    [Fact]
    public async Task NestedStrategyInsideHedging_EmitsPipelineNameTag()
    {
        // Correlate on operation.key, not pipeline.name: pipeline.name is the value under test, and
        // MeterListener is process-global, so filtering on it would either be circular or pick up
        // measurements from other test classes running in parallel.
        const string OperationKey = "nested-hedging-probe";

        var pipelineNames = new List<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ResilionTelemetry.MeterName
                    && instrument.Name == "resilion.retry.attempts")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string? pipelineName = null;
            var mine = false;
            foreach (var tag in tags)
            {
                if (tag.Key == ResilionTelemetry.OperationKeyTag && (tag.Value as string) == OperationKey)
                {
                    mine = true;
                }
                else if (tag.Key == ResilionTelemetry.PipelineNameTag)
                {
                    pipelineName = tag.Value as string;
                }
            }

            if (mine)
            {
                lock (pipelineNames) { pipelineNames.Add(pipelineName); }
            }
        });
        listener.Start();

        var attempt = 0;
        var pipeline = Pipeline.Create<string>(b =>
        {
            b.Name = "nested-pipeline";
            // MaxHedgedAttempts must be >= 2 so a hedge is actually launched: only then does the
            // inner retry run on hedging's per-attempt context, which is the thing under test.
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                ShouldHandle = o => o.TryGetResult(out var r) && r == "fail",
            });
            b.AddRetry(new RetryStrategyOptions<string>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
                ShouldHandle = o => o.TryGetResult(out var r) && r == "fail",
            });
        });

        // ExecuteOutcomeAsync takes a caller-owned context, which is the only way to set OperationKey.
        var context = ResilienceContextPool.Shared.Rent();
        try
        {
            context.OperationKey = OperationKey;
            await pipeline.ExecuteOutcomeAsync(
                // Fail enough times that the inner retry exhausts inside the first hedged attempt
                // and hedging then launches a second attempt, whose retry also emits.
                static (state, ctx) => new ValueTask<Outcome<string>>(
                    Outcome<string>.FromResult(++state.Attempt < 5 ? "fail" : "ok")),
                new Counter(),
                context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        // The retry lives inside hedging, so its measurements come from a per-attempt context.
        // Before the fix those carried pipeline.name = null.
        Assert.NotEmpty(pipelineNames);
        Assert.All(pipelineNames, n => Assert.Equal("nested-pipeline", n));
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

    private sealed class Counter
    {
        public int Attempt;
    }

}
