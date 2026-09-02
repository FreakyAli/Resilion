using Microsoft.Extensions.DependencyInjection;
using Resilion.Extensions;
using Xunit;

namespace Resilion.Extensions.Tests;

public class ResiliencePipelineRegistryTests
{
    [Fact]
    public void RegisterAndGet_ReturnsSamePipeline()
    {
        using var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("test", b => b.AddRetry());

        var p1 = registry.GetPipeline("test");
        var p2 = registry.GetPipeline("test");

        Assert.NotNull(p1);
        Assert.Same(p1, p2);
    }

    [Fact]
    public void GetPipeline_UnregisteredKey_Throws()
    {
        using var registry = new ResiliencePipelineRegistry<string>();

        Assert.Throws<KeyNotFoundException>(() => registry.GetPipeline("missing"));
    }

    [Fact]
    public void GetPipeline_FailedLookupThenRegisterAndRetry_Succeeds()
    {
        // Regression test: Ensure failed lookups don't cache a faulted Lazy entry
        using var registry = new ResiliencePipelineRegistry<string>();

        // First attempt: unregistered key should throw
        Assert.Throws<KeyNotFoundException>(() => registry.GetPipeline("retry-cache"));

        // Register the pipeline after the failed attempt
        registry.RegisterPipeline("retry-cache", b => b.AddRetry());

        // Second attempt: should now succeed instead of replaying the cached failure
        var pipeline = registry.GetPipeline("retry-cache");
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void GetPipeline_Typed_FailedLookupThenRegisterAndRetry_Succeeds()
    {
        // Regression test: Ensure typed lookups don't cache a faulted Lazy entry
        using var registry = new ResiliencePipelineRegistry<string>();

        // First attempt: unregistered typed key should throw
        Assert.Throws<KeyNotFoundException>(() => registry.GetPipeline<int>("typed-retry-cache"));

        // Register the typed pipeline after the failed attempt
        registry.RegisterPipeline<int>("typed-retry-cache", b =>
            b.AddFallback(new FallbackStrategyOptions<int> { FallbackAction = 42 }));

        // Second attempt: should now succeed instead of replaying the cached failure
        var pipeline = registry.GetPipeline<int>("typed-retry-cache");
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void RegisterDuplicateKey_Throws()
    {
        using var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("dup", b => b.AddTimeout(TimeSpan.FromSeconds(5)));

        Assert.Throws<ArgumentException>(() =>
            registry.RegisterPipeline("dup", b => b.AddRetry()));
    }

    [Fact]
    public void TryGetPipeline_Registered_ReturnsTrue()
    {
        using var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("exists", b => b.AddRetry());

        Assert.True(registry.TryGetPipeline("exists", out var pipeline));
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void TryGetPipeline_NotRegistered_ReturnsFalse()
    {
        using var registry = new ResiliencePipelineRegistry<string>();

        Assert.False(registry.TryGetPipeline("missing", out var pipeline));
        Assert.Null(pipeline);
    }

    [Fact]
    public async Task RegisteredPipeline_ExecutesCorrectly()
    {
        using var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("retry-3", b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
        }));

        var pipeline = registry.GetPipeline("retry-3");
        var callCount = 0;

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
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task TypedPipeline_RegisterAndResolve()
    {
        using var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline<int>("typed", b => b.AddFallback(
            new FallbackStrategyOptions<int> { FallbackAction = -1 }));

        var pipeline = registry.GetPipeline<int>("typed");
        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new Exception("fail");
            return new ValueTask<int>(0);
        });

        Assert.Equal(-1, result);
    }

    [Fact]
    public void Dispose_DisposesCreatedPipelines()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("x", b => b.AddTimeout(TimeSpan.FromSeconds(1)));
        _ = registry.GetPipeline("x"); // Force creation

        // Dispose should not throw.
        registry.Dispose();
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddResilienceServices_RegistersRegistry()
    {
        var services = new ServiceCollection();
        services.AddResilienceServices();

        var sp = services.BuildServiceProvider();
        var registry = sp.GetService<ResiliencePipelineRegistry<string>>();

        Assert.NotNull(registry);
    }

    [Fact]
    public void AddResilienceServices_RegistersContextPool()
    {
        var services = new ServiceCollection();
        services.AddResilienceServices();

        var sp = services.BuildServiceProvider();
        var pool = sp.GetService<ResilienceContextPool>();

        Assert.NotNull(pool);
        Assert.Same(ResilienceContextPool.Shared, pool);
    }

    [Fact]
    public void AddResilienceServices_RegistersPipelineProviderAsSameInstanceAsRegistry()
    {
        var services = new ServiceCollection();
        services.AddResilienceServices();

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ResiliencePipelineRegistry<string>>();
        var provider = sp.GetRequiredService<IPipelineProvider<string>>();

        Assert.Same(registry, provider);
    }

#pragma warning disable CS0618 // Testing the obsolete alias intentionally
    [Fact]
    public void AddResilion_ObsoleteAlias_StillRegistersServices()
    {
        var services = new ServiceCollection();
        services.AddResilion();

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<ResiliencePipelineRegistry<string>>());
        Assert.NotNull(sp.GetService<IPipelineProvider<string>>());
    }
#pragma warning restore CS0618

    [Fact]
    public async Task AddResiliencePipeline_ResolvesViaRegistry()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("http-retry", b => b
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
            })
            .AddTimeout(TimeSpan.FromSeconds(30)));

        var sp = services.BuildServiceProvider();

        // Apply configurators to the registry (simulating what a hosted service would do).
        var registry = ResilionServiceCollectionExtensions.BuildRegistry(sp);
        var pipeline = registry.GetPipeline("http-retry");

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task AddResiliencePipeline_Typed_ResolvesViaRegistry()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline<int>("fallback-pipeline", b => b
            .AddFallback(new FallbackStrategyOptions<int> { FallbackAction = 42 }));

        var sp = services.BuildServiceProvider();
        var registry = ResilionServiceCollectionExtensions.BuildRegistry(sp);
        var pipeline = registry.GetPipeline<int>("fallback-pipeline");

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new Exception("fail");
            return new ValueTask<int>(0);
        });
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task AddResiliencePipeline_DiRoundTrip_ResolvesViaPipelineProvider()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("http-retry", b => b
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = RetryDelay.None })
            .AddTimeout(TimeSpan.FromSeconds(30)));

        var sp = services.BuildServiceProvider();

        // Resolve through the full DI container — no manual BuildRegistry call.
        var provider = sp.GetRequiredService<IPipelineProvider<string>>();
        var pipeline = provider.GetPipeline("http-retry");

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task AddResiliencePipeline_Typed_DiRoundTrip_ResolvesViaPipelineProvider()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline<int>("fallback-pipeline", b => b
            .AddFallback(new FallbackStrategyOptions<int> { FallbackAction = 42 }));

        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IPipelineProvider<string>>();
        var pipeline = provider.GetPipeline<int>("fallback-pipeline");

        var result = await pipeline.ExecuteAsync(ct =>
        {
            throw new Exception("fail");
            return new ValueTask<int>(0);
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void AddResilienceServices_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddResilienceServices();
        services.AddResilienceServices();
        services.AddResilienceServices();

        var sp = services.BuildServiceProvider();
        var registries = sp.GetServices<ResiliencePipelineRegistry<string>>().ToList();

        Assert.Single(registries);
    }
}
