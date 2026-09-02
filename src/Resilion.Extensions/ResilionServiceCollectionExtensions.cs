using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Resilion.Extensions;

/// <summary>
/// Extension methods for registering Resilion services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ResilionServiceCollectionExtensions
{
    /// <summary>
    /// Adds Resilion core services to the service collection, including a shared
    /// <see cref="ResiliencePipelineRegistry{TKey}"/> with string keys and its read-only
    /// <see cref="IPipelineProvider{TKey}"/> view.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResilienceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ResiliencePipelineRegistry<string>>(sp => BuildRegistry(sp));
        services.TryAddSingleton<IPipelineProvider<string>>(sp =>
            sp.GetRequiredService<ResiliencePipelineRegistry<string>>());
        services.TryAddSingleton(ResilienceContextPool.Shared);
        return services;
    }

    /// <summary>
    /// Adds Resilion core services to the service collection. Obsolete alias for
    /// <see cref="AddResilienceServices"/> — kept so existing code continues to compile.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete("Use AddResilienceServices() instead. This alias will be removed in a future major version.")]
    public static IServiceCollection AddResilion(this IServiceCollection services)
        => services.AddResilienceServices();

    /// <summary>
    /// Registers a named resilience pipeline that is created lazily on first access
    /// and cached as a singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">A unique name for the pipeline.</param>
    /// <param name="configure">A delegate that configures the pipeline builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddResiliencePipeline("my-pipeline", builder =&gt; builder
    ///     .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    ///     .AddTimeout(TimeSpan.FromSeconds(10)));
    ///
    /// // Resolve later:
    /// var registry = serviceProvider.GetRequiredService&lt;ResiliencePipelineRegistry&lt;string&gt;&gt;();
    /// var pipeline = registry.GetPipeline("my-pipeline");
    /// </code>
    /// </example>
    public static IServiceCollection AddResiliencePipeline(
        this IServiceCollection services,
        string name,
        Action<PipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddResilienceServices();

        // Register a post-configuration action that runs when the registry is first resolved.
        var capturedName = name;
        var capturedConfigure = configure;
        services.AddSingleton<IPipelineConfigurator>(
            new PipelineConfigurator(capturedName, capturedConfigure));

        return services;
    }

    /// <summary>
    /// Registers a named typed resilience pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type for the pipeline.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">A unique name for the pipeline.</param>
    /// <param name="configure">A delegate that configures the typed pipeline builder.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResiliencePipeline<TResult>(
        this IServiceCollection services,
        string name,
        Action<PipelineBuilder<TResult>> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddResilienceServices();

        services.AddSingleton<IPipelineConfigurator>(
            new TypedPipelineConfigurator<TResult>(name, configure));

        return services;
    }

    /// <summary>
    /// Builds and returns the <see cref="ResiliencePipelineRegistry{TKey}"/> with all registered
    /// pipeline configurations applied. Call this after all <c>AddResiliencePipeline</c> calls.
    /// </summary>
    internal static ResiliencePipelineRegistry<string> BuildRegistry(IServiceProvider sp)
    {
        // Create a new registry instance (don't call GetRequiredService to avoid infinite recursion)
        var registry = new ResiliencePipelineRegistry<string>();

        // Apply all registered pipeline configurations
        var configurators = sp.GetServices<IPipelineConfigurator>();
        foreach (var configurator in configurators)
        {
            configurator.Configure(registry);
        }

        return registry;
    }
}

internal interface IPipelineConfigurator
{
    void Configure(ResiliencePipelineRegistry<string> registry);
}

internal sealed class PipelineConfigurator : IPipelineConfigurator
{
    private readonly string _name;
    private readonly Action<PipelineBuilder> _configure;

    public PipelineConfigurator(string name, Action<PipelineBuilder> configure)
    {
        _name = name;
        _configure = configure;
    }

    public void Configure(ResiliencePipelineRegistry<string> registry)
        => registry.RegisterPipeline(_name, _configure);
}

internal sealed class TypedPipelineConfigurator<TResult> : IPipelineConfigurator
{
    private readonly string _name;
    private readonly Action<PipelineBuilder<TResult>> _configure;

    public TypedPipelineConfigurator(string name, Action<PipelineBuilder<TResult>> configure)
    {
        _name = name;
        _configure = configure;
    }

    public void Configure(ResiliencePipelineRegistry<string> registry)
        => registry.RegisterPipeline(_name, _configure);
}
