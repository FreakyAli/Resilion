namespace Resilion;

/// <summary>
/// Extension methods for adding the Hedging strategy to a typed pipeline builder.
/// </summary>
/// <remarks>
/// Hedging is only available on typed pipelines because it must produce a result of the correct type.
/// </remarks>
public static class HedgingExtensions
{
    /// <summary>
    /// Adds a hedging strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The hedging options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddHedging<TResult>(
        this PipelineBuilder<TResult> builder,
        HedgingStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new HedgingStrategy<TResult>(options, builder.TimeProvider), Internal.StrategyType.Hedging);
    }
}
