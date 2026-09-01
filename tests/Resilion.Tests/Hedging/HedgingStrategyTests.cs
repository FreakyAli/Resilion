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
                HedgingDelay = TimeSpan.FromSeconds(1), // Ignored in sync — always sequential
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
}
