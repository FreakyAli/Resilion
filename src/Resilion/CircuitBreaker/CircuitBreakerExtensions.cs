namespace Resilion;

/// <summary>
/// Extension methods for adding the Circuit Breaker strategy to a pipeline builder.
/// </summary>
public static class CircuitBreakerExtensions
{
    /// <summary>
    /// Adds an exception-only circuit breaker strategy with the specified options.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The circuit breaker options. When null, uses defaults.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder AddCircuitBreaker(
        this PipelineBuilder builder,
        CircuitBreakerStrategyOptions? options = null)
    {
        options ??= new CircuitBreakerStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new CircuitBreakerStrategy(options, builder.TimeProvider), Internal.StrategyType.CircuitBreaker);
    }

    /// <summary>
    /// Adds a result-aware circuit breaker strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The typed circuit breaker options.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddCircuitBreaker<TResult>(
        this PipelineBuilder<TResult> builder,
        CircuitBreakerStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new CircuitBreakerTypedStrategy<TResult>(options, builder.TimeProvider), Internal.StrategyType.CircuitBreaker);
    }

    /// <summary>
    /// Adds an exception-only circuit breaker strategy to a typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The circuit breaker options. When null, uses defaults.</param>
    /// <returns>The builder for chaining.</returns>
    public static PipelineBuilder<TResult> AddCircuitBreaker<TResult>(
        this PipelineBuilder<TResult> builder,
        CircuitBreakerStrategyOptions? options = null)
    {
        options ??= new CircuitBreakerStrategyOptions();
        options.Validate();
        return builder.AddStrategy(new CircuitBreakerStrategy(options, builder.TimeProvider), Internal.StrategyType.CircuitBreaker);
    }
}
