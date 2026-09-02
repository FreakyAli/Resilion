using Xunit;

namespace Resilion.Tests;

public class FallbackStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Happy path — no fallback triggered
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_Success_NoFallback()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "default",
            }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public void Sync_Success_NoFallback()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "default",
            }));

        var result = pipeline.Execute(ct => "ok");
        Assert.Equal("ok", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Constant value fallback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_ConstantFallback_ReturnedOnException()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "fallback-value",
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new InvalidOperationException("fail");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("fallback-value", result);
    }

    [Fact]
    public void Sync_ConstantFallback_ReturnedOnException()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "fallback-value",
            }));

        var result = pipeline.Execute(ct =>
        {
            throw new InvalidOperationException("fail");
            return "unreachable";
        });

        Assert.Equal("fallback-value", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Sync factory fallback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_SyncFactory_ComputesFallback()
    {
        Func<FallbackContext<string>, string> factory =
            ctx => $"fallback-for-{ctx.Exception?.GetType().Name}";

        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = factory,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new TimeoutException("timed out");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("fallback-for-TimeoutException", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Async factory fallback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_AsyncFactory_ComputesFallback()
    {
        Func<FallbackContext<string>, ValueTask<string>> factory =
            async ctx =>
            {
                await Task.Yield();
                return "async-fallback";
            };

        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = factory,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new InvalidOperationException("fail");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("async-fallback", result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Result-based fallback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_FallbackOnBadResult()
    {
        var pipeline = Pipeline.Create<int>(b => b.AddFallback(
            new FallbackStrategyOptions<int>
            {
                FallbackAction = 0,
                ShouldHandle = outcome =>
                    outcome.TryGetResult(out var val) && val < 0,
            }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(-1));
        Assert.Equal(0, result);

        result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // OperationCanceledException — not handled by default
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OCE_NotHandledByDefault()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "fallback",
            }));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                throw new OperationCanceledException();
                return new ValueTask<string>("unreachable");
            }).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // OnFallback callback
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OnFallback_IsFired()
    {
        OnFallbackEvent<string>? capturedEvent = null;
        Action<OnFallbackEvent<string>> onFallback = e => capturedEvent = e;

        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = "replacement",
                OnFallback = onFallback,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new InvalidOperationException("fail");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("replacement", result);
        Assert.NotNull(capturedEvent);
        Assert.Equal("replacement", capturedEvent.Value.FallbackResult);
        Assert.IsType<InvalidOperationException>(capturedEvent.Value.Outcome.Exception);
    }

    // ──────────────────────────────────────────────────────────────────
    // Composition — Fallback wrapping Retry
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FallbackWrappingRetry_FallbackCatchesExhaustedRetries()
    {
        var callCount = 0;

        var pipeline = Pipeline.Create<string>(b => b
            .AddFallback(new FallbackStrategyOptions<string>
            {
                FallbackAction = "exhausted-fallback",
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = RetryDelay.None,
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            callCount++;
            throw new InvalidOperationException("always fail");
            return new ValueTask<string>("unreachable");
        });

        Assert.Equal("exhausted-fallback", result);
        Assert.Equal(3, callCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // Options validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingFallbackAction_ThrowsAtBuildTime()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Pipeline.Create<string>(b => b.AddFallback(
                new FallbackStrategyOptions<string>())));
    }

    // ──────────────────────────────────────────────────────────────────
    // Fallback factory that throws — exception propagates
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_FallbackFactoryThrows_ExceptionPropagates()
    {
        Func<FallbackContext<string>, string> factory =
            _ => throw new NotImplementedException("factory broke");

        var pipeline = Pipeline.Create<string>(b => b.AddFallback(
            new FallbackStrategyOptions<string>
            {
                FallbackAction = factory,
            }));

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                throw new InvalidOperationException("trigger");
                return new ValueTask<string>("unreachable");
            }).AsTask());
    }
}
