using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Resilion.Extensions.Tests;

/// <summary>
/// Dynamic pipeline reload via <see cref="IOptionsMonitor{TOptions}"/> — future-plans #41.
/// </summary>
public class PipelineReloadTests
{
    private sealed class RetryOptions
    {
        public int MaxRetryAttempts { get; set; } = 1;
    }

    /// <summary>
    /// Drives change notifications without a file watcher: a memory configuration source plus
    /// <c>Reload()</c> is the smallest thing that makes <c>IOptionsMonitor.OnChange</c> fire.
    /// </summary>
    private static (ServiceProvider Provider, IConfigurationRoot Config) BuildProvider(
        Action<IServiceCollection> configureServices,
        string initialAttempts = "1")
    {
        var source = new Dictionary<string, string?> { ["Retry:MaxRetryAttempts"] = initialAttempts };
        var config = new ConfigurationBuilder().AddInMemoryCollection(source).Build();

        var services = new ServiceCollection();
        services.Configure<RetryOptions>(config.GetSection("Retry"));
        configureServices(services);

        return (services.BuildServiceProvider(), config);
    }

    private static void SetAttempts(IConfigurationRoot config, string value)
    {
        config["Retry:MaxRetryAttempts"] = value;
        config.Reload();
    }

    // ─── Reload rebuilds ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OptionsChange_NextGetPipelineReturnsRebuiltPipeline()
    {
        var (provider, config) = BuildProvider(s =>
            s.AddResiliencePipeline<RetryOptions>("api", (b, o) => b.AddRetry(
                new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts, Delay = RetryDelay.None })));
        using var scope = provider;

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();

        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.GetPipeline("api").ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));
        Assert.Equal(2, attempts); // 1 initial + 1 retry

        SetAttempts(config, "3");

        attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.GetPipeline("api").ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));
        Assert.Equal(4, attempts); // 1 initial + 3 retries
    }

    [Fact]
    public void ConfigureDelegate_ReadsFreshOptionsOnEveryRebuild()
    {
        var builds = 0;
        var (provider, config) = BuildProvider(s =>
            s.AddResiliencePipeline<RetryOptions>("api", (b, o) =>
            {
                builds++;
                b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts });
            }));
        using var scope = provider;

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();

        _ = registry.GetPipeline("api");
        _ = registry.GetPipeline("api");
        Assert.Equal(1, builds); // cached

        SetAttempts(config, "2");
        _ = registry.GetPipeline("api");
        Assert.Equal(2, builds); // rebuilt once, not per access
    }

    [Fact]
    public async Task Async_InFlightExecution_CompletesOnTheOldPipeline()
    {
        var (provider, config) = BuildProvider(s =>
            s.AddResiliencePipeline<RetryOptions>("api", (b, o) => b.AddRetry(
                new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts, Delay = RetryDelay.None })));
        using var scope = provider;

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();
        var pipeline = registry.GetPipeline("api");

        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var execution = pipeline.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await release.Task;
            return "ok";
        }).AsTask();

        await entered.Task;
        SetAttempts(config, "9");          // invalidates while the execution is mid-flight
        release.SetResult();

        Assert.Equal("ok", await execution);
    }

    // ─── Invalidation semantics ─────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidatePipeline_UnknownKey_ReturnsFalse()
    {
        var registry = new ResiliencePipelineRegistry<string>();

        Assert.False(registry.InvalidatePipeline("nope"));
    }

    [Fact]
    public void InvalidatePipeline_RegisteredButNeverBuilt_ReturnsFalseAndDoesNotInvokeCallback()
    {
        var invoked = 0;
        var registry = new ResiliencePipelineRegistry<string>
        {
            OnPipelineReplaced = _ => invoked++,
        };
        registry.RegisterPipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        // Nothing cached yet, so there is nothing to replace — the next access builds a current one.
        Assert.False(registry.InvalidatePipeline("api"));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void OnPipelineReplaced_ReceivesKeyResultTypeAndOldPipeline()
    {
        PipelineReplacedArgs<string>? seen = null;
        var registry = new ResiliencePipelineRegistry<string>
        {
            OnPipelineReplaced = args => seen = args,
        };
        registry.RegisterPipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        var original = registry.GetPipeline("api");
        Assert.True(registry.InvalidatePipeline("api"));

        Assert.NotNull(seen);
        Assert.Equal("api", seen!.Value.Key);
        Assert.Null(seen.Value.ResultType);                       // non-generic
        Assert.Same(original, seen.Value.ReplacedPipeline);
    }

    [Fact]
    public void OnPipelineReplaced_Typed_ReportsResultType()
    {
        PipelineReplacedArgs<string>? seen = null;
        var registry = new ResiliencePipelineRegistry<string>
        {
            OnPipelineReplaced = args => seen = args,
        };
        registry.RegisterPipeline<int>("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        _ = registry.GetPipeline<int>("api");
        Assert.True(registry.InvalidatePipeline<int>("api"));

        Assert.NotNull(seen);
        Assert.Equal(typeof(int), seen!.Value.ResultType);
    }

    [Fact]
    public async Task Async_ReplacedPipeline_IsNotDisposedByTheRegistry()
    {
        // The registry cannot know when in-flight executions finish, so disposal is the caller's
        // call. An evicted pipeline must still be usable.
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(30)));

        var original = registry.GetPipeline("api");
        registry.InvalidatePipeline("api");

        Assert.Equal("ok", await original.ExecuteAsync(ct => new ValueTask<string>("ok")));
    }

    [Fact]
    public void InvalidatePipeline_Typed_OnlyAffectsThatResultType()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline<int>("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));
        registry.RegisterPipeline<string>("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        var intPipeline = registry.GetPipeline<int>("api");
        var stringPipeline = registry.GetPipeline<string>("api");

        Assert.True(registry.InvalidatePipeline<int>("api"));

        Assert.NotSame(intPipeline, registry.GetPipeline<int>("api"));
        Assert.Same(stringPipeline, registry.GetPipeline<string>("api"));
    }

    [Fact]
    public void FactoryIsPreserved_AcrossInvalidation()
    {
        // Reload works precisely because the factory survives; builders are single-use.
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(registry.GetPipeline("api"));
            registry.InvalidatePipeline("api");
        }

        Assert.NotNull(registry.GetPipeline("api"));
    }

    // ─── TryGetPipeline<TResult> ────────────────────────────────────────────────────────────

    [Fact]
    public void TryGetPipelineTyped_RegisteredKey_ReturnsTrue()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline<int>("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        Assert.True(registry.TryGetPipeline<int>("api", out var pipeline));
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void TryGetPipelineTyped_WrongResultType_ReturnsFalseAndNull()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.RegisterPipeline<int>("api", b => b.AddTimeout(TimeSpan.FromSeconds(1)));

        Assert.False(registry.TryGetPipeline<string>("api", out var pipeline));
        Assert.Null(pipeline);
    }

    // ─── Wiring ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistryConfigurators_RunBeforePipelineConfigurators()
    {
        // Ordering matters: a registry-level setting must be in place before any pipeline can be
        // built, or there is a window where one is built without it.
        var replaced = 0;
        var (provider, config) = BuildProvider(s =>
        {
            s.ConfigureResiliencePipelineRegistry(r => r.OnPipelineReplaced = _ => replaced++);
            s.AddResiliencePipeline<RetryOptions>("api", (b, o) => b.AddRetry(
                new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts }));
        });
        using var scope = provider;

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();
        _ = registry.GetPipeline("api");

        SetAttempts(config, "2");

        Assert.Equal(1, replaced);
    }

    [Fact]
    public void NamedOptions_OptionsNameOverload_BindsTheNamedInstance()
    {
        var services = new ServiceCollection();
        services.Configure<RetryOptions>("fast", o => o.MaxRetryAttempts = 7);
        services.AddResiliencePipeline<RetryOptions>("api", "fast", (b, o) =>
            b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts, Delay = RetryDelay.None }));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();

        var attempts = 0;
        Assert.Throws<InvalidOperationException>(() =>
            registry.GetPipeline("api").Execute<string>(ct =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));

        Assert.Equal(8, attempts); // 1 initial + 7 retries
    }

    [Fact]
    public void RegistryDispose_DisposesReloadSubscriptions()
    {
        var (provider, config) = BuildProvider(s =>
            s.AddResiliencePipeline<RetryOptions>("api", (b, o) => b.AddRetry(
                new RetryStrategyOptions { MaxRetryAttempts = o.MaxRetryAttempts })));

        var registry = provider.GetRequiredService<ResiliencePipelineRegistry<string>>();
        _ = registry.GetPipeline("api");

        registry.Dispose();

        // A change arriving after teardown must not throw out of the notification callback.
        SetAttempts(config, "5");
        provider.Dispose();
    }

    [Fact]
    public async Task Async_FailedLookupStillDoesNotPoisonCache_AfterInvalidate()
    {
        // Regression guard on the pre-existing invariant, re-checked around the new code path.
        var registry = new ResiliencePipelineRegistry<string>();

        Assert.Throws<KeyNotFoundException>(() => registry.GetPipeline("late"));

        registry.RegisterPipeline("late", b => b.AddTimeout(TimeSpan.FromSeconds(30)));
        Assert.Equal("ok", await registry.GetPipeline("late").ExecuteAsync(ct => new ValueTask<string>("ok")));

        registry.InvalidatePipeline("late");
        Assert.Equal("ok", await registry.GetPipeline("late").ExecuteAsync(ct => new ValueTask<string>("ok")));
    }
}
