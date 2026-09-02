namespace Resilion;

/// <summary>
/// Extension methods for adding the Fallback strategy to a typed pipeline builder.
/// </summary>
/// <remarks>
/// Fallback is only available on typed pipelines because it must produce a substitute
/// value of the correct type.
/// </remarks>
public static class FallbackExtensions
{
    /// <summary>
    /// Adds a fallback strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The fallback options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddFallback<TResult>(
        this PipelineBuilder<TResult> builder,
        FallbackStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new FallbackStrategy<TResult>(options), Internal.StrategyType.Fallback);
    }
}
