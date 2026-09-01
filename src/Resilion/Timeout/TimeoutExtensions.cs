namespace Resilion;

/// <summary>
/// Extension methods for adding the Timeout strategy to a pipeline builder.
/// </summary>
public static class TimeoutExtensions
{
    /// <summary>
    /// Adds a timeout strategy with the specified duration.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder AddTimeout(this PipelineBuilder builder, TimeSpan timeout)
        => builder.AddTimeout(new TimeoutStrategyOptions { Timeout = timeout });

    /// <summary>
    /// Adds a timeout strategy with the specified options.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The timeout options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder AddTimeout(this PipelineBuilder builder, TimeoutStrategyOptions? options = null)
    {
        options ??= new TimeoutStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new TimeoutStrategy(options, builder.TimeProvider), Internal.StrategyType.Timeout);
    }

    /// <summary>
    /// Adds a timeout strategy with the specified duration to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type of the pipeline.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddTimeout<TResult>(this PipelineBuilder<TResult> builder, TimeSpan timeout)
        => builder.AddTimeout(new TimeoutStrategyOptions { Timeout = timeout });

    /// <summary>
    /// Adds a timeout strategy with the specified options to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type of the pipeline.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The timeout options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddTimeout<TResult>(this PipelineBuilder<TResult> builder, TimeoutStrategyOptions? options = null)
    {
        options ??= new TimeoutStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new TimeoutStrategy(options, builder.TimeProvider), Internal.StrategyType.Timeout);
    }
}
