namespace Resilion.Chaos;

/// <summary>
/// Adds the chaos fault strategy to a pipeline.
/// </summary>
public static class ChaosFaultExtensions
{
    /// <summary>Adds a chaos fault strategy.</summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The fault options.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder AddChaosFault(
        this PipelineBuilder builder,
        ChaosFaultStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new ChaosFaultStrategy(options), Resilion.Internal.StrategyType.Chaos);
    }

    /// <summary>Adds a chaos fault strategy with a rate and a fault factory.</summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="injectionRate">The probability of injecting, from 0.0 to 1.0.</param>
    /// <param name="faultFactory">Produces the exception to throw, called per injection.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder AddChaosFault(
        this PipelineBuilder builder,
        double injectionRate,
        Func<Exception> faultFactory)
    {
        ArgumentNullException.ThrowIfNull(faultFactory);
        return builder.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = injectionRate,
            FaultGenerator = _ => faultFactory(),
        });
    }

    /// <inheritdoc cref="AddChaosFault(Resilion.PipelineBuilder,Resilion.Chaos.ChaosFaultStrategyOptions)"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public static PipelineBuilder<TResult> AddChaosFault<TResult>(
        this PipelineBuilder<TResult> builder,
        ChaosFaultStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new ChaosFaultStrategy(options), Resilion.Internal.StrategyType.Chaos);
    }

    /// <inheritdoc cref="AddChaosFault(Resilion.PipelineBuilder,double,System.Func{System.Exception})"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public static PipelineBuilder<TResult> AddChaosFault<TResult>(
        this PipelineBuilder<TResult> builder,
        double injectionRate,
        Func<Exception> faultFactory)
    {
        ArgumentNullException.ThrowIfNull(faultFactory);
        return builder.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = injectionRate,
            FaultGenerator = _ => faultFactory(),
        });
    }
}
