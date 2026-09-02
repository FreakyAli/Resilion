namespace Resilion.RateLimiting;

/// <summary>
/// Extension methods for adding the Rate Limiter strategy to a pipeline builder.
/// </summary>
public static class RateLimiterExtensions
{
    /// <summary>
    /// Adds a rate limiter strategy with the specified options.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The rate limiter options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder AddRateLimiter(
        this PipelineBuilder builder,
        RateLimiterStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new RateLimiterStrategy(options), Resilion.Internal.StrategyType.RateLimiter);
    }

    /// <summary>
    /// Adds a rate limiter strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The rate limiter options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddRateLimiter<TResult>(
        this PipelineBuilder<TResult> builder,
        RateLimiterStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new RateLimiterStrategy(options), Resilion.Internal.StrategyType.RateLimiter);
    }
}
