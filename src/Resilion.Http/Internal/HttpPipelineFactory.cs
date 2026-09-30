using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Resilion.RateLimiting;

namespace Resilion.Http.Internal;

/// <summary>
/// Builds the HTTP resilience pipelines from their options.
/// </summary>
internal static class HttpPipelineFactory
{
    /// <summary>
    /// Configures the standard pipeline: RateLimiter → TotalRequestTimeout → Retry →
    /// CircuitBreaker → AttemptTimeout.
    /// </summary>
    /// <remarks>
    /// Runs inside the registry's lazy factory, so the caller's <paramref name="configure"/>
    /// delegate is invoked once, on first use of the client, not at registration time.
    /// <para>
    /// This order passes Resilion's ordering validation with no errors and no warnings, so
    /// <c>ThrowOnOrderingErrors</c> is left at its default and warnings are not suppressed — if a
    /// future change makes the shape questionable, the build should say so.
    /// </para>
    /// </remarks>
    internal static void ConfigureStandard(
        PipelineBuilder<HttpResponseMessage> builder,
        Action<HttpStandardResilienceOptions, IServiceProvider>? configure,
        IServiceProvider services)
    {
        var options = new HttpStandardResilienceOptions
        {
            TimeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System,
        };

        configure?.Invoke(options, services);
        options.Validate();

        // Must precede every Add*: each extension captures builder.TimeProvider at add time into
        // the strategy it creates, so setting it afterwards would have no effect.
        builder.TimeProvider = options.TimeProvider;

        if (options.RateLimiter is not null)
        {
            builder.AddRateLimiter(options.RateLimiter);
        }

        builder.AddTimeout(options.TotalRequestTimeout);
        builder.AddRetry(WithHttpDefaults(options.Retry));
        builder.AddCircuitBreaker(WithHttpDefaults(options.CircuitBreaker));
        builder.AddTimeout(options.AttemptTimeout);

        // Do not touch builder.Name — the registry already set it to the pipeline key, which is
        // what makes the pipeline.name telemetry tag read "<client>/standard".
    }

    /// <summary>
    /// Configures the standard hedging pipeline: RateLimiter → TotalRequestTimeout → Hedging →
    /// CircuitBreaker → AttemptTimeout.
    /// </summary>
    internal static void ConfigureHedging(
        PipelineBuilder<HttpResponseMessage> builder,
        Action<HttpStandardHedgingOptions, IServiceProvider>? configure,
        IServiceProvider services)
    {
        var options = new HttpStandardHedgingOptions
        {
            TimeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System,
        };

        configure?.Invoke(options, services);
        options.Validate();

        builder.TimeProvider = options.TimeProvider;

        if (options.RateLimiter is not null)
        {
            builder.AddRateLimiter(options.RateLimiter);
        }

        builder.AddTimeout(options.TotalRequestTimeout);
        builder.AddHedging(WithHttpDefaults(options.Hedging));
        builder.AddCircuitBreaker(WithHttpDefaults(options.CircuitBreaker));
        builder.AddTimeout(options.AttemptTimeout);
    }

    /// <summary>
    /// Configures a caller-defined pipeline, pre-seeding the time provider from the container so
    /// a registered <c>TimeProvider</c> works here too.
    /// </summary>
    internal static void ConfigureCustom(
        PipelineBuilder<HttpResponseMessage> builder,
        Action<PipelineBuilder<HttpResponseMessage>, IServiceProvider> configure,
        IServiceProvider services)
    {
        builder.TimeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        configure(builder, services);
    }

    /// <summary>
    /// Substitutes the HTTP transient predicate when the caller left <c>ShouldHandle</c> null.
    /// </summary>
    /// <remarks>
    /// This is the single place the HTTP default is applied. Without it, replacing an options
    /// record wholesale — <c>o.Retry = new() { MaxRetryAttempts = 5 }</c> — would silently drop
    /// 5xx/408/429 handling and leave a retry that only fires on exceptions.
    /// </remarks>
    private static RetryStrategyOptions<HttpResponseMessage> WithHttpDefaults(
        RetryStrategyOptions<HttpResponseMessage> options)
        => options.ShouldHandle is null
            ? options with { ShouldHandle = HttpResiliencePredicates.IsTransient }
            : options;

    /// <inheritdoc cref="WithHttpDefaults(RetryStrategyOptions{HttpResponseMessage})"/>
    private static CircuitBreakerStrategyOptions<HttpResponseMessage> WithHttpDefaults(
        CircuitBreakerStrategyOptions<HttpResponseMessage> options)
        => options.ShouldHandle is null
            ? options with { ShouldHandle = HttpResiliencePredicates.IsTransient }
            : options;

    /// <inheritdoc cref="WithHttpDefaults(RetryStrategyOptions{HttpResponseMessage})"/>
    private static HedgingStrategyOptions<HttpResponseMessage> WithHttpDefaults(
        HedgingStrategyOptions<HttpResponseMessage> options)
        => options.ShouldHandle is null
            ? options with { ShouldHandle = HttpResiliencePredicates.IsTransient }
            : options;
}
