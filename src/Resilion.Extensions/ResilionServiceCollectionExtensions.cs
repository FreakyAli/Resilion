using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        AddKeyedPipeline(services, name);

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
        AddKeyedPipelineOfT<TResult>(services, name);

        return services;
    }

    /// <summary>
    /// Registers a named resilience pipeline built from <typeparamref name="TOptions"/>, rebuilt
    /// automatically whenever those options change.
    /// </summary>
    /// <typeparam name="TOptions">The options type to bind the pipeline to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The pipeline name.</param>
    /// <param name="configure">Configures the builder from the current options value.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <paramref name="configure"/> runs on every build and receives the options value current at
    /// that moment, so it must read from its argument rather than capturing anything.
    /// <para>
    /// <strong>Resolve the pipeline per call.</strong> A reload replaces the cached instance, so a
    /// consumer that caches it in a field keeps executing the old one forever. Depend on
    /// <see cref="IPipelineProvider{TKey}"/> and call <c>GetPipeline</c> each time; it is a
    /// dictionary lookup.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddResiliencePipeline<TOptions>(
        this IServiceCollection services,
        string name,
        Action<PipelineBuilder, TOptions> configure)
        where TOptions : class
        => services.AddResiliencePipeline(name, optionsName: null, configure);

    /// <summary>
    /// Registers a named resilience pipeline built from a named <typeparamref name="TOptions"/>
    /// instance, rebuilt automatically whenever those options change.
    /// </summary>
    /// <typeparam name="TOptions">The options type to bind the pipeline to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The pipeline name.</param>
    /// <param name="optionsName">
    /// The named options instance to read, or <see langword="null"/> for the default instance.
    /// </param>
    /// <param name="configure">Configures the builder from the current options value.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResiliencePipeline<TOptions>(
        this IServiceCollection services,
        string name,
        string? optionsName,
        Action<PipelineBuilder, TOptions> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddResilienceServices();
        services.AddOptions();
        services.AddSingleton<IPipelineConfigurator>(
            new MonitoredPipelineConfigurator<TOptions>(name, optionsName, configure));
        AddKeyedPipeline(services, name);

        return services;
    }

    /// <summary>
    /// Registers a named typed resilience pipeline built from <typeparamref name="TOptions"/>,
    /// rebuilt automatically whenever those options change.
    /// </summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <typeparam name="TOptions">The options type to bind the pipeline to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The pipeline name.</param>
    /// <param name="configure">Configures the builder from the current options value.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResiliencePipeline<TResult, TOptions>(
        this IServiceCollection services,
        string name,
        Action<PipelineBuilder<TResult>, TOptions> configure)
        where TOptions : class
        => services.AddResiliencePipeline<TResult, TOptions>(name, optionsName: null, configure);

    /// <summary>
    /// Registers a named typed resilience pipeline built from a named
    /// <typeparamref name="TOptions"/> instance, rebuilt automatically whenever those options
    /// change.
    /// </summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <typeparam name="TOptions">The options type to bind the pipeline to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The pipeline name.</param>
    /// <param name="optionsName">
    /// The named options instance to read, or <see langword="null"/> for the default instance.
    /// </param>
    /// <param name="configure">Configures the builder from the current options value.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResiliencePipeline<TResult, TOptions>(
        this IServiceCollection services,
        string name,
        string? optionsName,
        Action<PipelineBuilder<TResult>, TOptions> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddResilienceServices();
        services.AddOptions();
        services.AddSingleton<IPipelineConfigurator>(
            new MonitoredTypedPipelineConfigurator<TResult, TOptions>(name, optionsName, configure));
        AddKeyedPipelineOfT<TResult>(services, name);

        return services;
    }

    /// <summary>
    /// Registers the pipeline as a keyed singleton, so it can be injected with
    /// <c>[FromKeyedServices(name)]</c> instead of going through the registry.
    /// </summary>
    /// <remarks>
    /// Always on: it costs one <see cref="ServiceDescriptor"/> per pipeline, adds no Resilion API
    /// (the consumer-facing surface is the BCL's <c>FromKeyedServicesAttribute</c>), and an opt-in
    /// flag would mean a boolean on every overload.
    /// <para>
    /// Registered with <c>TryAddKeyedSingleton</c> so a duplicate <c>AddResiliencePipeline</c> call
    /// does not add a second descriptor — the registry already rejects duplicate keys, which is
    /// where that error belongs.
    /// </para>
    /// <para>
    /// Singleton is the only correct lifetime. Keyed transient would have DI track and dispose the
    /// instance per scope, destroying the registry's cached pipeline while other callers still
    /// hold it.
    /// </para>
    /// <para>
    /// <strong>Incompatible with reload.</strong> A keyed singleton resolves once and an injected
    /// field caches it again, so neither observes
    /// <see cref="ResiliencePipelineRegistry{TKey}.InvalidatePipeline(TKey)"/>. Keyed injection is
    /// for static pipelines; a reloadable pipeline must be resolved per call through
    /// <see cref="IPipelineProvider{TKey}"/>.
    /// </para>
    /// </remarks>
    private static void AddKeyedPipeline(IServiceCollection services, string name)
        => services.TryAddKeyedSingleton<Pipeline>(name, static (sp, key) =>
            sp.GetRequiredService<ResiliencePipelineRegistry<string>>().GetPipeline((string)key!));

    /// <inheritdoc cref="AddKeyedPipeline(IServiceCollection, string)"/>
    private static void AddKeyedPipelineOfT<TResult>(IServiceCollection services, string name)
        => services.TryAddKeyedSingleton<Pipeline<TResult>>(name, static (sp, key) =>
            sp.GetRequiredService<ResiliencePipelineRegistry<string>>()
                .GetPipeline<TResult>((string)key!));

    /// <summary>
    /// Configures the <see cref="ResiliencePipelineRegistry{TKey}"/> itself, before any pipeline
    /// factory is registered on it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the registry.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Use this for registry-level concerns such as
    /// <see cref="ResiliencePipelineRegistry{TKey}.OnPipelineReplaced"/>. It runs before pipeline
    /// registration, so settings are in place by the time the first pipeline can be built.
    /// </remarks>
    public static IServiceCollection ConfigureResiliencePipelineRegistry(
        this IServiceCollection services,
        Action<ResiliencePipelineRegistry<string>> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddResilienceServices();
        services.AddSingleton<IRegistryConfigurator>(new RegistryConfigurator(configure));

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

        // Registry-level configuration runs FIRST and deliberately so: it sets things a pipeline
        // build can observe (OnPipelineReplaced, and any future registry default), so applying it
        // after factory registration would leave a window where a pipeline is built without it.
        foreach (var configurator in sp.GetServices<IRegistryConfigurator>())
        {
            configurator.Configure(registry);
        }

        // Apply all registered pipeline configurations
        var configurators = sp.GetServices<IPipelineConfigurator>();
        foreach (var configurator in configurators)
        {
            configurator.Configure(registry, sp);
        }

        return registry;
    }
}

internal interface IPipelineConfigurator
{
    /// <param name="registry">The registry being built.</param>
    /// <param name="services">
    /// The provider the registry is being resolved from. Needed by configurators that read
    /// <see cref="IOptionsMonitor{TOptions}"/>; ignored by the static ones.
    /// </param>
    void Configure(ResiliencePipelineRegistry<string> registry, IServiceProvider services);
}

/// <summary>Applies registry-level configuration before any pipeline factory is registered.</summary>
internal interface IRegistryConfigurator
{
    void Configure(ResiliencePipelineRegistry<string> registry);
}

internal sealed class RegistryConfigurator : IRegistryConfigurator
{
    private readonly Action<ResiliencePipelineRegistry<string>> _configure;

    public RegistryConfigurator(Action<ResiliencePipelineRegistry<string>> configure)
        => _configure = configure;

    public void Configure(ResiliencePipelineRegistry<string> registry) => _configure(registry);
}

/// <summary>
/// Registers a pipeline factory that reads <typeparamref name="TOptions"/> at build time, and
/// invalidates the cached pipeline whenever those options change.
/// </summary>
internal sealed class MonitoredPipelineConfigurator<TOptions> : IPipelineConfigurator
    where TOptions : class
{
    private readonly string _name;
    private readonly string? _optionsName;
    private readonly Action<PipelineBuilder, TOptions> _configure;

    public MonitoredPipelineConfigurator(
        string name,
        string? optionsName,
        Action<PipelineBuilder, TOptions> configure)
    {
        _name = name;
        _optionsName = optionsName;
        _configure = configure;
    }

    public void Configure(ResiliencePipelineRegistry<string> registry, IServiceProvider services)
    {
        var monitor = services.GetRequiredService<IOptionsMonitor<TOptions>>();

        // Read through the monitor on every build rather than capturing a value, so a rebuild
        // after a change picks up the new options.
        registry.RegisterPipeline(_name, builder => _configure(builder, CurrentValue(monitor)));

        // IOptionsMonitor<T>.OnChange is declared as returning IDisposable?, and a custom monitor
        // implementation may legitimately return null. The registry owns whatever we get.
        var subscription = monitor.OnChange((_, _) => registry.InvalidatePipeline(_name));
        if (subscription is not null)
        {
            registry.AddReloadSubscription(subscription);
        }
    }

    private TOptions CurrentValue(IOptionsMonitor<TOptions> monitor)
        => _optionsName is null ? monitor.CurrentValue : monitor.Get(_optionsName);
}

/// <summary>The typed counterpart of <see cref="MonitoredPipelineConfigurator{TOptions}"/>.</summary>
internal sealed class MonitoredTypedPipelineConfigurator<TResult, TOptions> : IPipelineConfigurator
    where TOptions : class
{
    private readonly string _name;
    private readonly string? _optionsName;
    private readonly Action<PipelineBuilder<TResult>, TOptions> _configure;

    public MonitoredTypedPipelineConfigurator(
        string name,
        string? optionsName,
        Action<PipelineBuilder<TResult>, TOptions> configure)
    {
        _name = name;
        _optionsName = optionsName;
        _configure = configure;
    }

    public void Configure(ResiliencePipelineRegistry<string> registry, IServiceProvider services)
    {
        var monitor = services.GetRequiredService<IOptionsMonitor<TOptions>>();

        registry.RegisterPipeline<TResult>(_name, builder => _configure(builder, CurrentValue(monitor)));

        var subscription = monitor.OnChange((_, _) => registry.InvalidatePipeline<TResult>(_name));
        if (subscription is not null)
        {
            registry.AddReloadSubscription(subscription);
        }
    }

    private TOptions CurrentValue(IOptionsMonitor<TOptions> monitor)
        => _optionsName is null ? monitor.CurrentValue : monitor.Get(_optionsName);
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

    public void Configure(ResiliencePipelineRegistry<string> registry, IServiceProvider services)
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

    public void Configure(ResiliencePipelineRegistry<string> registry, IServiceProvider services)
        => registry.RegisterPipeline(_name, _configure);
}
