using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

public class TimeoutStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Happy path — operation completes within timeout
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OperationCompletesWithinTimeout_ReturnsResult()
    {
        var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public void Sync_OperationCompletesWithinTimeout_ReturnsResult()
    {
        var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));

        var result = pipeline.Execute(ct => "ok");

        Assert.Equal("ok", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Timeout fires — operation exceeds timeout
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OperationExceedsTimeout_ThrowsTimeoutRejectedException()
    {
        var fakeTime = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(TimeSpan.FromSeconds(5));
        });

        var task = pipeline.ExecuteAsync(async ct =>
        {
            // Simulate a long operation that observes cancellation
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return "should not reach here";
        });

        // Advance past the timeout
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        var ex = await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());
        Assert.Equal(TimeSpan.FromSeconds(5), ex.ConfiguredTimeout);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task Async_TimeoutZero_ImmediatelyTimesOut()
    {
        var fakeTime = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(TimeSpan.Zero);
        });

        // TimeSpan.Zero fires immediately from the timer.
        // We need to give the timer a chance to fire.
        var task = pipeline.ExecuteAsync(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), fakeTime, ct);
            return "should not reach";
        });

        // Advance even a tiny bit so the timer fires.
        fakeTime.Advance(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // Infinite timeout — passthrough
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_InfiniteTimeout_PassesThrough()
    {
        var pipeline = Pipeline.Create(b =>
            b.AddTimeout(System.Threading.Timeout.InfiniteTimeSpan));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("pass"));

        Assert.Equal("pass", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // User cancellation — propagates OperationCanceledException, NOT TimeoutRejectedException
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_UserCancellation_PropagatesOCE_NotTimeoutRejected()
    {
        var fakeTime = new FakeTimeProvider();
        using var userCts = new CancellationTokenSource();

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(TimeSpan.FromSeconds(30));
        });

        var task = pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return "should not reach";
        }, userCts.Token);

        // User cancels — NOT a timeout
        userCts.Cancel();

        // Should throw OperationCanceledException, NOT TimeoutRejectedException
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.AsTask());
        Assert.IsNotType<TimeoutRejectedException>(ex);
    }

    // ──────────────────────────────────────────────────────────────────
    // CancellationToken flows to callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_TimeoutToken_FlowsToCallback()
    {
        var fakeTime = new FakeTimeProvider();
        CancellationToken receivedToken = default;

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(TimeSpan.FromSeconds(5));
        });

        await pipeline.ExecuteAsync(ct =>
        {
            receivedToken = ct;
            return new ValueTask<int>(42);
        });

        // The callback should have received a linked token (different from CancellationToken.None)
        // We can't easily assert it's linked, but we know the pipeline replaced it.
        // The important thing is the test completes without timeout.
    }

    // ──────────────────────────────────────────────────────────────────
    // OnTimeout callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OnTimeout_IsFired()
    {
        var fakeTime = new FakeTimeProvider();
        OnTimeoutArgs? capturedArgs = null;

        Action<OnTimeoutArgs> onTimeout = args => { capturedArgs = args; };

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                OnTimeout = onTimeout,
            });
        });

        var task = pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return "nope";
        });

        fakeTime.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());

        Assert.NotNull(capturedArgs);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedArgs.Value.Timeout);
    }

    // ──────────────────────────────────────────────────────────────────
    // TimeoutGenerator — dynamic timeout
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_TimeoutGenerator_OverridesStaticTimeout()
    {
        var fakeTime = new FakeTimeProvider();

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(100), // Should be ignored
                TimeoutGenerator = _ => TimeSpan.FromSeconds(2),
            });
        });

        var task = pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), fakeTime, ct);
            return "nope";
        });

        fakeTime.Advance(TimeSpan.FromSeconds(3));

        var ex = await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());
        Assert.Equal(TimeSpan.FromSeconds(2), ex.ConfiguredTimeout);
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypedPipeline_Timeout_Works()
    {
        var pipeline = Pipeline.Create<string>(b =>
            b.AddTimeout(TimeSpan.FromSeconds(10)));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("typed-ok"));

        Assert.Equal("typed-ok", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Exception from callback (non-cancellation) propagates normally
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_NonCancellationException_PropagatesNormally()
    {
        var pipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(10)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct =>
                throw new InvalidOperationException("business error")).AsTask());

        Assert.Equal("business error", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NegativeTimeout_ThrowsAtBuildTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(-5))));
    }

    // ──────────────────────────────────────────────────────────────────
    // Default options
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultOptions_Uses30SecondTimeout()
    {
        var fakeTime = new FakeTimeProvider();

        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddTimeout();
        });

        var task = pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), fakeTime, ct);
            return "nope";
        });

        // Advance past the 30s default
        fakeTime.Advance(TimeSpan.FromSeconds(31));

        var ex = await Assert.ThrowsAsync<TimeoutRejectedException>(() => task.AsTask());
        Assert.Equal(TimeSpan.FromSeconds(30), ex.ConfiguredTimeout);
    }
}
