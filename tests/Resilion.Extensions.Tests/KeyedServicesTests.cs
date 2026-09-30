using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Resilion.Extensions.Tests;

/// <summary>
/// Keyed DI resolution of named pipelines — future-plans #42. Adds no Resilion API; the
/// consumer-facing surface is the BCL's <c>[FromKeyedServices]</c>.
/// </summary>
public class KeyedServicesTests
{
    private sealed class Consumer
    {
        public Consumer([FromKeyedServices("api")] Pipeline pipeline) => Pipeline = pipeline;

        public Pipeline Pipeline { get; }
    }

    private sealed class RetryOptions
    {
        public int MaxRetryAttempts { get; set; } = 1;
    }

    // ─── Resolution ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_KeyedSingleton_Untyped_ResolvesAndExecutes()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredKeyedService<Pipeline>("api");

        Assert.Equal("ok", await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok")));
    }

    [Fact]
    public async Task Async_KeyedSingleton_Typed_ResolvesAndExecutes()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline<string>("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredKeyedService<Pipeline<string>>("api");

        Assert.Equal("ok", await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok")));
    }

    [Fact]
    public void KeyedSingleton_IsSameInstanceAsRegistryGetPipeline()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        var viaKey = provider.GetRequiredKeyedService<Pipeline>("api");
        var viaRegistry = provider.GetRequiredService<IPipelineProvider<string>>().GetPipeline("api");

        Assert.Same(viaRegistry, viaKey);
    }

    [Fact]
    public void KeyedSingleton_ResolvedTwice_IsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredKeyedService<Pipeline>("api"),
            provider.GetRequiredKeyedService<Pipeline>("api"));
    }

    [Fact]
    public void KeyedSingleton_TwoNames_ResolveDifferentPipelines()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("a", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        services.AddResiliencePipeline("b", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        Assert.NotSame(
            provider.GetRequiredKeyedService<Pipeline>("a"),
            provider.GetRequiredKeyedService<Pipeline>("b"));
    }

    [Fact]
    public void KeyedSingleton_SameNameDifferentResultTypes_ResolveIndependently()
    {
        // Typed pipelines are keyed by name AND result type in the registry; keyed DI mirrors that
        // because the service type differs.
        var services = new ServiceCollection();
        services.AddResiliencePipeline<int>("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        services.AddResiliencePipeline<string>("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredKeyedService<Pipeline<int>>("api"));
        Assert.NotNull(provider.GetRequiredKeyedService<Pipeline<string>>("api"));
    }

    [Fact]
    public void KeyedSingleton_UnregisteredKey_Throws()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<Pipeline>("nope"));
    }

    [Fact]
    public async Task Async_FromKeyedServices_ConstructorInjection_ResolvesPipeline()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        services.AddSingleton<Consumer>();
        using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<Consumer>();

        Assert.Equal("ok", await consumer.Pipeline.ExecuteAsync(ct => new ValueTask<string>("ok")));
    }

    [Fact]
    public void AddResiliencePipeline_CalledTwiceSameName_RegistersOneKeyedDescriptor()
    {
        // TryAddKeyedSingleton, so the duplicate-key error stays with the registry where it belongs.
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));

        var keyed = services.Count(d =>
            d.IsKeyedService && Equals(d.ServiceKey, "api") && d.ServiceType == typeof(Pipeline));

        Assert.Equal(1, keyed);
    }

    // ─── Disposal ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderDispose_WithKeyedPipeline_DoesNotThrow()
    {
        // The container disposes the keyed singleton AND the registry disposes every pipeline it
        // created, so the same instance is disposed twice. Idempotent Dispose makes that safe.
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredKeyedService<Pipeline>("api");

        provider.Dispose();
    }

    [Fact]
    public void RegistryDisposeThenProviderDispose_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();
        _ = provider.GetRequiredKeyedService<Pipeline>("api");

        registry.Dispose();
        provider.Dispose();
    }

    // ─── The documented incompatibility with reload ─────────────────────────────────────────

    [Fact]
    public void KeyedPipeline_DoesNotObserveInvalidation_DocumentedLimitation()
    {
        // Inherent, not a bug: a keyed singleton resolves once for the container's lifetime, so it
        // cannot see a reload. This test exists so the limitation is pinned rather than discovered.
        // Reloadable pipelines must be resolved per call via IPipelineProvider.
        var source = new Dictionary<string, string?> { ["Retry:MaxRetryAttempts"] = "1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(source).Build();

        var services = new ServiceCollection();
        services.Configure<RetryOptions>(config.GetSection("Retry"));
        services.AddResiliencePipeline<RetryOptions>("api", (b, o) =>
            b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts }));
        using var provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<Pipeline>("api");
        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();
        var beforeReload = registry.GetPipeline("api");

        config["Retry:MaxRetryAttempts"] = "5";
        config.Reload();

        // The registry hands out a rebuilt pipeline...
        Assert.NotSame(beforeReload, registry.GetPipeline("api"));

        // ...but the keyed singleton is still the original instance.
        Assert.Same(keyed, provider.GetRequiredKeyedService<Pipeline>("api"));
        Assert.Same(beforeReload, keyed);
    }
}
