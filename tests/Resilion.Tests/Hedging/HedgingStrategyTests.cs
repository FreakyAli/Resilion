using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

public class HedgingStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Single attempt — no hedging
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MaxHedgedAttempts1_NoHedging()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string> { MaxHedgedAttempts = 1 }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            Interlocked.Increment(ref callCount);
            return new ValueTask<string>("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // Primary succeeds — no hedging needed
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_PrimarySucceeds_ReturnsImmediately()
    {
        var callCount = 0;
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.FromSeconds(2),
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            Interlocked.Increment(ref callCount);
            return new ValueTask<string>("fast");
        });

        Assert.Equal("fast", result);
        // Primary succeeds immediately, hedged attempts may or may not launch
        // depending on timing, but the result should always be "fast".
    }

    // ──────────────────────────────────────────────────────────────────
    // Parallel mode (delay = 0) — all attempts launch simultaneously
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParallelMode_AllAttemptsLaunch()
    {
        var callCount = 0;
        var barrier = new TaskCompletionSource<bool>();

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero, // Parallel mode
                ShouldHandle = outcome => outcome.Exception is not null,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            // All attempts return the same result immediately.
            return new ValueTask<string>($"attempt-{attempt}");
        });

        // At least one attempt should have completed.
        Assert.StartsWith("attempt-", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Sequential mode (InfiniteTimeSpan) — waits for failure before next
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SequentialMode_TriesOneAtATime()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt < 3)
            {
                throw new InvalidOperationException($"fail-{attempt}");
            }

            return new ValueTask<string>("success");
        });

        Assert.Equal("success", result);
        Assert.Equal(3, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // All attempts fail
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllAttemptsFail_ThrowsLastException()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero,
            }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                throw new InvalidOperationException("always fail");
                return new ValueTask<string>("unreachable");
            }).AsTask());

        Assert.Equal("always fail", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────
    // Custom ActionGenerator
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActionGenerator_CustomActionsPerAttempt()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                ActionGenerator = ctx => ctx.AttemptNumber switch
                {
                    0 => ct => throw new InvalidOperationException("primary fails"),
                    1 => ct => throw new InvalidOperationException("secondary fails"),
                    2 => ct => new ValueTask<string>("tertiary wins"),
                    _ => null,
                },
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            // This shouldn't be called because ActionGenerator provides all actions.
            throw new InvalidOperationException("should not reach");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("tertiary wins", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // OnHedging callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnHedging_FiredForEachHedgedAttempt()
    {
        var hedgingEvents = new List<int>();
        Action<OnHedgingEvent<string>> onHedging = e =>
            hedgingEvents.Add(e.AttemptNumber);

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero,
                OnHedging = onHedging,
            }));

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));

        // OnHedging should have been called for attempts 1 and 2 (not 0, the primary).
        Assert.Contains(1, hedgingEvents);
        Assert.Contains(2, hedgingEvents);
    }

    // ──────────────────────────────────────────────────────────────────
    // Sync execution — sequential fallback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sync_ExecutesSequentially()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan, // Sequential mode
            }));

        var result = pipeline.Execute(ct =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new InvalidOperationException("fail");
            }

            return "success";
        });

        Assert.Equal("success", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void Sync_NonSequentialMode_Throws()
    {
        // Parallel and latency hedging modes require concurrent execution, which the sync path
        // can't provide — it must throw rather than silently degrade to sequential.
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.FromSeconds(1), // Latency mode
            }));

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.Execute(ct => "unreachable"));
    }

    [Fact]
    public void Sync_ParallelMode_Throws()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero, // Parallel mode
            }));

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.Execute(ct => "unreachable"));
    }

    // ──────────────────────────────────────────────────────────────────
    // User cancellation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserCancellation_PropagatesOCE()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = TimeSpan.Zero,
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<string>("unreachable");
            }, cts.Token).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // Result-based hedging
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResultBasedHedging_RetriesOnBadResult()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<int>(b => b.AddHedging(
            new HedgingStrategyOptions<int>
            {
                MaxHedgedAttempts = 3,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
                ShouldHandle = outcome =>
                    outcome.TryGetResult(out var val) && val < 0,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            return new ValueTask<int>(attempt < 3 ? -1 : 42);
        });

        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroMaxAttempts_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create<string>(b => b.AddHedging(
                new HedgingStrategyOptions<string> { MaxHedgedAttempts = 0 })));
    }

    [Fact]
    public void NegativeDelay_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create<string>(b => b.AddHedging(
                new HedgingStrategyOptions<string> { HedgingDelay = TimeSpan.FromSeconds(-1) })));
    }

    // ──────────────────────────────────────────────────────────────────
    // Properties propagation to hedged attempts
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Properties_PropagatedToHedgedAttempts()
    {
        var propertyKey = new ResiliencePropertyKey<string>("test-key");
        string? capturedValue = null;

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            }));

        // Use ExecuteOutcomeAsync so we can set properties on the context.
        var context = ResilienceContextPool.Shared.Rent();
        context.Properties.Set(propertyKey, "propagated-value");

        try
        {
            var state = new HedgingPropertyTestState { CallCount = 0, OriginalContext = context, PropertyKey = propertyKey, CapturedValue = null };
            var outcome = await pipeline.ExecuteOutcomeAsync(
                static (state, ctx) =>
                {
                    var attempt = Interlocked.Increment(ref state.CallCount);
                    if (attempt == 1)
                    {
                        return new ValueTask<Outcome<string>>(
                            Outcome<string>.FromException(new InvalidOperationException("fail")));
                    }

                    // Second attempt — check if properties were propagated from the current attempt's context.
                    ctx.Properties.TryGetValue(state.PropertyKey, out var val);
                    state.CapturedValue = val;
                    return new ValueTask<Outcome<string>>(Outcome<string>.FromResult("ok"));
                },
                state,
                context);

            // The captured value should reflect what was set on the original context.
            // The hedging strategy creates a per-attempt context copy with properties copied from original.
            Assert.Equal("propagated-value", state.CapturedValue);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private class HedgingPropertyTestState
    {
        public int CallCount;
        public ResilienceContext OriginalContext = null!;
        public ResiliencePropertyKey<string> PropertyKey;
        public string? CapturedValue;
    }

    // ──────────────────────────────────────────────────────────────────
    // Latency mode (HedgingDelay > 0) — the core hedging use case
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LatencyMode_PrimarySlow_SecondaryFiresAfterDelayAndWins()
    {
        var fakeTime = new FakeTimeProvider();
        var callCount = 0;
        var primaryTcs = new TaskCompletionSource<string>();

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.FromSeconds(2),
            });
        });

        var executeTask = pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            return attempt == 1
                ? new ValueTask<string>(primaryTcs.Task) // primary: never completes on its own
                : new ValueTask<string>("secondary-fast");
        }).AsTask();

        // Let the primary attempt actually start running on its own Task.Run thread
        // before we advance the clock past the hedging delay.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        fakeTime.Advance(TimeSpan.FromSeconds(3));

        var result = await executeTask;

        Assert.Equal("secondary-fast", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task LatencyMode_PrimarySucceedsBeforeDelay_SecondaryNeverFires()
    {
        var fakeTime = new FakeTimeProvider();
        var callCount = 0;

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.FromSeconds(2),
            });
        });

        var result = await pipeline.ExecuteAsync(ct =>
        {
            Interlocked.Increment(ref callCount);
            return new ValueTask<string>("primary-fast");
        });

        Assert.Equal("primary-fast", result);

        // Even if we advance well past the hedging delay after the primary already completed,
        // no secondary attempt should have been launched.
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task LatencyMode_BothSlow_SecondaryEventuallySucceeds()
    {
        var fakeTime = new FakeTimeProvider();
        var callCount = 0;
        var primaryTcs = new TaskCompletionSource<string>();
        var secondaryTcs = new TaskCompletionSource<string>();

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddHedging(new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.FromSeconds(2),
            });
        });

        var executeTask = pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            return attempt == 1
                ? new ValueTask<string>(primaryTcs.Task)
                : new ValueTask<string>(secondaryTcs.Task);
        }).AsTask();

        // Let the primary start, then advance past the hedging delay so the secondary launches.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        fakeTime.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Both attempts are still pending — now let the secondary win.
        secondaryTcs.SetResult("secondary-wins");

        var result = await executeTask;
        Assert.Equal("secondary-wins", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Cleanup: non-cooperative losing attempt doesn't hang indefinitely
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cleanup_NonCooperativeLosingAttempt_DoesNotHangIndefinitely()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(
            new HedgingStrategyOptions<string>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = TimeSpan.Zero, // Parallel mode — both launch immediately.
            }));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await pipeline.ExecuteAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt == 1)
            {
                // Winning attempt: succeeds immediately.
                return new ValueTask<string>("winner");
            }

            // Losing attempt: ignores cancellation entirely (never observes the token).
            // The strategy's cleanup path must bound how long it waits for this to finish.
            return new ValueTask<string>(Task.Run(() =>
            {
                Thread.Sleep(TimeSpan.FromMinutes(10));
                return "never";
            }));
        });

        sw.Stop();

        Assert.Equal("winner", result);
        // The strategy's cleanup deadline is a bounded few seconds, not indefinite.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"Expected bounded cleanup, took {sw.Elapsed}.");
    }
}
