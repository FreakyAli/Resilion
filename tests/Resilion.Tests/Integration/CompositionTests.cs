using System.Threading.RateLimiting;
using Resilion.RateLimiting;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// Integration tests exercising multiple strategies composed together, rather than each
/// strategy in isolation.
/// </summary>
public class CompositionTests
{
    [Fact]
    public async Task CanonicalFiveStrategyPipeline_ExecutesSuccessfully()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

        var visited = new List<string>();

        var pipeline = Pipeline.Create(b => b
            .AddRateLimiter(new RateLimiterStrategyOptions { RateLimiter = limiter })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 2, Delay = RetryDelay.None })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
            .AddTimeout(TimeSpan.FromSeconds(5)));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            visited.Add("user-code");
            return new ValueTask<string>("ok");
        });

        Assert.Equal("ok", result);
        Assert.Single(visited);
    }

    [Fact]
    public async Task CanonicalFiveStrategyPipeline_RetryRecoversFromTransientFailure()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

        var attempts = 0;

        var pipeline = Pipeline.Create(b => b
            .AddRateLimiter(new RateLimiterStrategyOptions { RateLimiter = limiter })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = RetryDelay.None })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions { MinimumThroughput = 100 })
            .AddTimeout(TimeSpan.FromSeconds(5)));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            attempts++;
            return attempts < 3
                ? throw new InvalidOperationException("transient")
                : new ValueTask<string>("recovered");
        });

        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task AddPipeline_ComposesTwoPrebuiltPipelines_BothRun()
    {
        var innerCalls = new List<string>();
        Action<RetryAttemptEvent> onInnerRetry = e => innerCalls.Add("inner-retry");

        var innerRetryPipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
            OnRetry = onInnerRetry,
        }));

        var innerTimeoutPipeline = Pipeline.Create(b => b.AddTimeout(TimeSpan.FromSeconds(30)));

        var composed = Pipeline.Create(b => b
            .AddPipeline(innerTimeoutPipeline)
            .AddPipeline(innerRetryPipeline));

        var attempts = 0;
        var result = await composed.ExecuteAsync(ct =>
        {
            attempts++;
            return attempts < 2
                ? throw new InvalidOperationException("fail once")
                : new ValueTask<string>("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Contains("inner-retry", innerCalls);
    }

    [Fact]
    public async Task AddPipeline_TypedComposition_BothRun()
    {
        var innerFallbackPipeline = Pipeline.Create<int>(b => b.AddFallback(new FallbackStrategyOptions<int>
        {
            FallbackAction = -1,
        }));

        var innerRetryPipeline = Pipeline.Create<int>(b => b.AddRetry(new RetryStrategyOptions<int>
        {
            MaxRetryAttempts = 1,
            Delay = RetryDelay.None,
        }));

        var composed = Pipeline.Create<int>(b => b
            .AddPipeline(innerRetryPipeline)
            .AddPipeline(innerFallbackPipeline));

        // Retry is outer, Fallback is inner — Fallback catches the failure before Retry ever
        // sees it, so the composed pipeline still substitutes -1 for a call that always fails.
        var result = await composed.ExecuteAsync(ct => throw new InvalidOperationException("always fails"));

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task AddPipeline_DisposesParentChainButNotComposedPipeline()
    {
        // Track disposal of strategies in composed pipeline
        var innerDisposed = false;
        var innerStrategy = new DisposableStrategy(() => { innerDisposed = true; });
        var innerPipeline = Pipeline.Create(b => b.AddStrategy(innerStrategy));

        // Track disposal of strategies in outer pipeline
        var outerDisposed = false;
        var outerStrategy = new DisposableStrategy(() => { outerDisposed = true; });

        var composed = Pipeline.Create(b => b
            .AddPipeline(innerPipeline)
            .AddStrategy(outerStrategy));

        // Before disposal, nothing is disposed
        Assert.False(innerDisposed);
        Assert.False(outerDisposed);

        // Dispose the composed pipeline
        composed.Dispose();

        // The outer strategy should be disposed (parent chain)
        Assert.True(outerDisposed, "Outer strategy should be disposed");

        // The inner composed pipeline should NOT be disposed — it's shared and may be used elsewhere
        Assert.False(innerDisposed, "Composed inner pipeline should not be disposed (it's shared)");
    }

    /// <summary>
    /// Helper strategy that calls a callback on disposal.
    /// </summary>
    private sealed class DisposableStrategy : Strategy
    {
        private readonly Action _onDispose;

        public DisposableStrategy(Action onDispose) => _onDispose = onDispose;

        protected internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context) => callback(context);

        protected internal override Outcome<TResult> Execute<TResult>(
            Func<ResilienceContext, Outcome<TResult>> callback,
            ResilienceContext context) => callback(context);

        public override void Dispose() => _onDispose();
    }
}
