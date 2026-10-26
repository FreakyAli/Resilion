namespace Resilion.Chaos;

/// <summary>
/// Adds the chaos latency strategy to a pipeline.
/// </summary>
public static class ChaosLatencyExtensions
{
    /// <summary>Adds a chaos latency strategy.</summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The latency options.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder AddChaosLatency(
        this PipelineBuilder builder,
        ChaosLatencyStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(
            new ChaosLatencyStrategy(options, builder.TimeProvider), Resilion.Internal.StrategyType.Chaos);
    }

    /// <summary>Adds a chaos latency strategy with a rate and a fixed delay.</summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="injectionRate">The probability of injecting, from 0.0 to 1.0.</param>
    /// <param name="latency">The delay to inject.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder AddChaosLatency(
        this PipelineBuilder builder,
        double injectionRate,
        TimeSpan latency)
        => builder.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = injectionRate,
            Latency = latency,
        });

    /// <inheritdoc cref="AddChaosLatency(Resilion.PipelineBuilder,Resilion.Chaos.ChaosLatencyStrategyOptions)"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public static PipelineBuilder<TResult> AddChaosLatency<TResult>(
        this PipelineBuilder<TResult> builder,
        ChaosLatencyStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(
            new ChaosLatencyStrategy(options, builder.TimeProvider), Resilion.Internal.StrategyType.Chaos);
    }

    /// <inheritdoc cref="AddChaosLatency(Resilion.PipelineBuilder,double,System.TimeSpan)"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public static PipelineBuilder<TResult> AddChaosLatency<TResult>(
        this PipelineBuilder<TResult> builder,
        double injectionRate,
        TimeSpan latency)
        => builder.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = injectionRate,
            Latency = latency,
        });
}
