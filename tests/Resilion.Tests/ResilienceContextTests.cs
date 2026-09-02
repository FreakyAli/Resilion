using Xunit;

namespace Resilion.Tests;

public class ResilienceContextTests
{
    [Fact]
    public void Pool_RentAndReturn_ReusesInstances()
    {
        var pool = new ResilienceContextPool();
        using var cts = new CancellationTokenSource();

        var ctx1 = pool.Rent(cts.Token);
        Assert.Equal(cts.Token, ctx1.CancellationToken);

        pool.Return(ctx1);

        var ctx2 = pool.Rent();
        // Should get the same pooled instance back
        Assert.Same(ctx1, ctx2);
        // Should be reset
        Assert.Equal(CancellationToken.None, ctx2.CancellationToken);
        Assert.Null(ctx2.OperationKey);
        Assert.False(ctx2.ContinueOnCapturedContext);

        pool.Return(ctx2);
    }

    [Fact]
    public void Pool_Return_ResetsContext()
    {
        var pool = new ResilienceContextPool();
        using var cts = new CancellationTokenSource();

        var ctx = pool.Rent(cts.Token);
        ctx.OperationKey = "test-op";
        ctx.ContinueOnCapturedContext = true;
        ctx.Properties.Set(new ResiliencePropertyKey<string>("key"), "value");

        pool.Return(ctx);

        // Re-rent and verify everything is reset
        var ctx2 = pool.Rent();
        Assert.Same(ctx, ctx2);
        Assert.Null(ctx2.OperationKey);
        Assert.False(ctx2.ContinueOnCapturedContext);
        Assert.Equal(0, ctx2.Properties.Count);

        pool.Return(ctx2);
    }

    [Fact]
    public void Pool_SharedInstance_IsNotNull()
    {
        Assert.NotNull(ResilienceContextPool.Shared);
    }

    [Fact]
    public void Properties_SetAndGet()
    {
        var ctx = ResilienceContextPool.Shared.Rent();
        try
        {
            var key = new ResiliencePropertyKey<int>("retryCount");
            ctx.Properties.Set(key, 3);

            Assert.True(ctx.Properties.TryGetValue(key, out var value));
            Assert.Equal(3, value);
            Assert.Equal(3, ctx.Properties.GetValue(key));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(ctx);
        }
    }

    [Fact]
    public void Properties_MissingKey_ReturnsDefault()
    {
        var ctx = ResilienceContextPool.Shared.Rent();
        try
        {
            var key = new ResiliencePropertyKey<string>("missing");

            Assert.False(ctx.Properties.TryGetValue(key, out var value));
            Assert.Null(value);
            Assert.Equal("fallback", ctx.Properties.GetValue(key, "fallback"));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(ctx);
        }
    }

    [Fact]
    public void Properties_Remove()
    {
        var ctx = ResilienceContextPool.Shared.Rent();
        try
        {
            var key = new ResiliencePropertyKey<int>("temp");
            ctx.Properties.Set(key, 42);
            Assert.Equal(1, ctx.Properties.Count);

            Assert.True(ctx.Properties.Remove(key));
            Assert.Equal(0, ctx.Properties.Count);
            Assert.False(ctx.Properties.Remove(key));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(ctx);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Configurable pool cap
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomMaxPoolSize_DiscardsContextsBeyondTheCap()
    {
        var pool = new ResilienceContextPool(maxPoolSize: 2);

        var c1 = pool.Rent();
        var c2 = pool.Rent();
        var c3 = pool.Rent();

        pool.Return(c1);
        pool.Return(c2);
        pool.Return(c3); // cap is 2 — this one should be discarded, not pooled

        var rented = new HashSet<ResilienceContext>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < 3; i++)
        {
            rented.Add(pool.Rent());
        }

        // At most 2 of the 3 originally-returned contexts can have been reused —
        // the third was discarded on return.
        var reusedCount = new[] { c1, c2, c3 }.Count(rented.Contains);
        Assert.True(reusedCount <= 2, $"Expected at most 2 contexts reused, got {reusedCount}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMaxPoolSize_ThrowsAtConstruction(int maxPoolSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResilienceContextPool(maxPoolSize));
    }
}
