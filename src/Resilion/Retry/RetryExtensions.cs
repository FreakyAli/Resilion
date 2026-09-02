namespace Resilion;

/// <summary>
/// Extension methods for adding the Retry strategy to a pipeline builder.
/// </summary>
public static class RetryExtensions
{
    /// <summary>
    /// Adds an exception-only retry strategy with the specified options.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The retry options. When null, uses default options (3 retries, exponential backoff with jitter).</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder AddRetry(this PipelineBuilder builder, RetryStrategyOptions? options = null)
    {
        options ??= new RetryStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new RetryStrategy(options, builder.TimeProvider), Internal.StrategyType.Retry);
    }

    /// <summary>
    /// Adds a result-aware retry strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The typed retry options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddRetry<TResult>(
        this PipelineBuilder<TResult> builder,
        RetryStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new RetryStrategy<TResult>(options, builder.TimeProvider), Internal.StrategyType.Retry);
    }

    /// <summary>
    /// Adds an exception-only retry strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The retry options. When null, uses default options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddRetry<TResult>(
        this PipelineBuilder<TResult> builder,
        RetryStrategyOptions? options = null)
    {
        options ??= new RetryStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new RetryStrategy(options, builder.TimeProvider), Internal.StrategyType.Retry);
    }
}
