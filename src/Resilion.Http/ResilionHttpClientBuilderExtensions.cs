using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resilion;
using Resilion.Extensions;
using Resilion.Http;
using Resilion.Http.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds Resilion resilience pipelines to an <see cref="IHttpClientBuilder"/>.
/// </summary>
/// <remarks>
/// No method here takes an optional parameter, and that is deliberate: adding a parameter to a
/// method that already has one is source-compatible but <em>binary</em>-breaking, so a shared
/// internal package that wraps these and ships as a binary would need recompiling. Distinct
/// overloads cost nothing at the call site — <c>AddStandardResilienceHandler()</c> still compiles —
/// and keep RS0026/RS0027 clean on a surface that is about to be frozen.
/// <para>
/// Deliberately in the <c>Microsoft.Extensions.DependencyInjection</c> namespace, which is in
/// ASP.NET Core's implicit usings, so these methods are discoverable with no extra <c>using</c> —
/// the convention for <see cref="IHttpClientBuilder"/> extensions. This differs from
/// <c>Resilion.RateLimiting</c>, which keeps its extensions in its own namespace.
/// </para>
/// </remarks>
public static class ResilionHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds the standard resilience pipeline to the client: rate limiter (off by default), total
    /// timeout, retry, circuit breaker, and per-attempt timeout.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
        => AddStandardResilienceHandlerCore(builder, configure: null);

    /// <summary>
    /// Adds the standard resilience pipeline to the client, adjusting the defaults.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configure">Adjusts the defaults.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddStandardResilienceHandler(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddStandardResilienceHandlerCore(builder, (options, _) => configure(options));
    }

    /// <summary>
    /// Adds the standard resilience pipeline to the client, with access to the service provider
    /// when configuring it.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configure">
    /// Adjusts the defaults. The <see cref="IServiceProvider"/> is the <strong>root</strong>
    /// provider, so resolving a scoped service from it is a captive dependency — resolve
    /// singletons only.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddStandardResilienceHandler(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions, IServiceProvider> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddStandardResilienceHandlerCore(builder, configure);
    }

    private static IHttpClientBuilder AddStandardResilienceHandlerCore(
        IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions, IServiceProvider>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var pipelineKey = PipelineKey(builder, "standard");
        var accessor = Register(
            builder,
            pipelineKey,
            b => HttpPipelineFactory.ConfigureStandard(b, configure, AccessorServices(b, pipelineKey)));

        // RequestReplay lives on the options, which are not materialised until the pipeline is
        // built — but the handler needs it at construction. Resolve it by building the options
        // once more here rather than reaching into the pipeline.
        return AddHandler(builder, pipelineKey, accessor, sequentialAttempts: true, sp =>
        {
            var probe = new HttpStandardResilienceOptions
            {
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
            };
            configure?.Invoke(probe, sp);
            return probe.RequestReplay;
        });
    }

    /// <summary>
    /// Adds the standard hedging pipeline to the client: rate limiter (off by default), total
    /// timeout, hedging, circuit breaker, and per-attempt timeout.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Always clones the request per attempt, and therefore always buffers the request body.
    /// </remarks>
    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder)
        => AddStandardHedgingHandlerCore(builder, configure: null);

    /// <summary>
    /// Adds the standard hedging pipeline to the client, adjusting the defaults.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configure">Adjusts the defaults.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Always clones the request per attempt, and therefore always buffers the request body.
    /// </remarks>
    public static IHttpClientBuilder AddStandardHedgingHandler(
        this IHttpClientBuilder builder,
        Action<HttpStandardHedgingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddStandardHedgingHandlerCore(builder, (options, _) => configure(options));
    }

    /// <summary>
    /// Adds the standard hedging pipeline to the client, with access to the service provider when
    /// configuring it.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configure">
    /// Adjusts the defaults. The <see cref="IServiceProvider"/> is the <strong>root</strong>
    /// provider.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddStandardHedgingHandler(
        this IHttpClientBuilder builder,
        Action<HttpStandardHedgingOptions, IServiceProvider> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddStandardHedgingHandlerCore(builder, configure);
    }

    private static IHttpClientBuilder AddStandardHedgingHandlerCore(
        IHttpClientBuilder builder,
        Action<HttpStandardHedgingOptions, IServiceProvider>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var pipelineKey = PipelineKey(builder, "hedging");
        var accessor = Register(
            builder,
            pipelineKey,
            b => HttpPipelineFactory.ConfigureHedging(b, configure, AccessorServices(b, pipelineKey)));

        // Hedged attempts can overlap, so cloning is mandatory and eager disposal is unsafe.
        return AddHandler(
            builder, pipelineKey, accessor,
            sequentialAttempts: false,
            _ => HttpRequestReplay.CloneRequest);
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the client under the given key.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="key">A name for this pipeline, unique within the client.</param>
    /// <param name="configure">Builds the pipeline.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddResilienceHandler(
        this IHttpClientBuilder builder,
        string key,
        Action<PipelineBuilder<HttpResponseMessage>> configure)
        => builder.AddResilienceHandler(key, configure, HttpRequestReplay.ReuseRequest);

    /// <summary>
    /// Adds a custom resilience pipeline to the client under the given key, choosing how each
    /// attempt obtains its request.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="key">A name for this pipeline, unique within the client.</param>
    /// <param name="configure">Builds the pipeline.</param>
    /// <param name="requestReplay">Whether each attempt sends the original request or a clone.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddResilienceHandler(
        this IHttpClientBuilder builder,
        string key,
        Action<PipelineBuilder<HttpResponseMessage>> configure,
        HttpRequestReplay requestReplay)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddResilienceHandler(key, (b, _) => configure(b), requestReplay);
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the client under the given key, with access to the
    /// service provider when building it.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="key">A name for this pipeline, unique within the client.</param>
    /// <param name="configure">
    /// Builds the pipeline. The <see cref="IServiceProvider"/> is the <strong>root</strong>
    /// provider.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddResilienceHandler(
        this IHttpClientBuilder builder,
        string key,
        Action<PipelineBuilder<HttpResponseMessage>, IServiceProvider> configure)
        => builder.AddResilienceHandler(key, configure, HttpRequestReplay.ReuseRequest);

    /// <summary>
    /// Adds a custom resilience pipeline to the client under the given key, with access to the
    /// service provider and an explicit request-replay mode.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="key">A name for this pipeline, unique within the client.</param>
    /// <param name="configure">
    /// Builds the pipeline. The <see cref="IServiceProvider"/> is the <strong>root</strong>
    /// provider.
    /// </param>
    /// <param name="requestReplay">Whether each attempt sends the original request or a clone.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddResilienceHandler(
        this IHttpClientBuilder builder,
        string key,
        Action<PipelineBuilder<HttpResponseMessage>, IServiceProvider> configure,
        HttpRequestReplay requestReplay)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineKey = PipelineKey(builder, key);
        var accessor = Register(
            builder,
            pipelineKey,
            b => HttpPipelineFactory.ConfigureCustom(b, configure, AccessorServices(b, pipelineKey)));

        // A caller-built pipeline may contain hedging, so attempts cannot be assumed sequential.
        return AddHandler(builder, pipelineKey, accessor, sequentialAttempts: false, _ => requestReplay);
    }

    /// <summary>
    /// Keys the pipeline by client name and kind, e.g. <c>"api/standard"</c>.
    /// </summary>
    /// <remarks>
    /// The slash cannot appear in a C# identifier-shaped client name, so this is unlikely to
    /// collide with a hand-registered pipeline key. Typed pipelines are additionally keyed by
    /// result type in the registry, so even an identical key registered for a different
    /// <c>TResult</c> is a separate entry.
    /// </remarks>
    private static string PipelineKey(IHttpClientBuilder builder, string kind)
        => $"{builder.Name}/{kind}";

    private static RootProviderAccessor Register(
        IHttpClientBuilder builder,
        string pipelineKey,
        Action<PipelineBuilder<HttpResponseMessage>> configure)
    {
        ThrowIfKeyAlreadyRegistered(builder, pipelineKey);

        var accessor = new RootProviderAccessor();
        AccessorRegistry[pipelineKey] = accessor;

        builder.Services.TryAddSingleton<HttpResilienceRootProvider>();
        builder.Services.AddSingleton(new ResilionHttpPipelineMarker(pipelineKey));
        builder.Services.AddResiliencePipeline<HttpResponseMessage>(pipelineKey, configure);

        return accessor;
    }

    private static IHttpClientBuilder AddHandler(
        IHttpClientBuilder builder,
        string pipelineKey,
        RootProviderAccessor accessor,
        bool sequentialAttempts,
        Func<IServiceProvider, HttpRequestReplay> resolveReplay)
    {
        builder.AddHttpMessageHandler(sp =>
        {
            // Assigned immediately before GetPipeline, which is the only call that can trigger the
            // lazy build that reads it. See RootProviderAccessor for why this indirection exists.
            accessor.Services = sp.GetRequiredService<HttpResilienceRootProvider>().Services;

            var pipeline = sp.GetRequiredService<IPipelineProvider<string>>()
                .GetPipeline<HttpResponseMessage>(pipelineKey);

            return new ResilienceHandler(pipeline, resolveReplay(accessor.Services), sequentialAttempts);
        });

        return builder;
    }

    /// <summary>
    /// Maps a pipeline key to the accessor holding its root provider.
    /// </summary>
    /// <remarks>
    /// Static because the registry's factory signature carries no state of its own. Keyed by the
    /// pipeline key, which is unique per client per kind.
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RootProviderAccessor>
        AccessorRegistry = new();

    private static IServiceProvider AccessorServices(
        PipelineBuilder<HttpResponseMessage> builder,
        string pipelineKey)
        => AccessorRegistry.TryGetValue(pipelineKey, out var accessor) && accessor.Services is { } services
            ? services
            : throw new InvalidOperationException(
                $"The root service provider for pipeline '{pipelineKey}' was not available when " +
                "the pipeline was built. This should be unreachable: the handler factory assigns " +
                "it immediately before resolving the pipeline.");

    /// <summary>
    /// Fails at registration time on a duplicate key, rather than letting the registry throw on
    /// first resolve where the error is far from its cause.
    /// </summary>
    private static void ThrowIfKeyAlreadyRegistered(IHttpClientBuilder builder, string pipelineKey)
    {
        foreach (var descriptor in builder.Services)
        {
            if (descriptor.ServiceType == typeof(ResilionHttpPipelineMarker)
                && descriptor.ImplementationInstance is ResilionHttpPipelineMarker marker
                && marker.PipelineKey == pipelineKey)
            {
                throw new InvalidOperationException(
                    $"A Resilion resilience handler is already registered for HTTP client " +
                    $"'{builder.Name}' under the key '{pipelineKey}'. Use AddResilienceHandler " +
                    "with a distinct key to add a second pipeline to the same client.");
            }
        }
    }
}
