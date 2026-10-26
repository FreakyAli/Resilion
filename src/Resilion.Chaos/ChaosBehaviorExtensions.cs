namespace Resilion.Chaos;

/// <summary>
/// Adds the chaos behavior strategy to a pipeline.
/// </summary>
public static class ChaosBehaviorExtensions
{
    /// <summary>Adds a chaos behavior strategy.</summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="options">The behavior options.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder AddChaosBehavior(
        this PipelineBuilder builder,
        ChaosBehaviorStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new ChaosBehaviorStrategy(options), Resilion.Internal.StrategyType.Chaos);
    }

    /// <inheritdoc cref="AddChaosBehavior(Resilion.PipelineBuilder,Resilion.Chaos.ChaosBehaviorStrategyOptions)"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public static PipelineBuilder<TResult> AddChaosBehavior<TResult>(
        this PipelineBuilder<TResult> builder,
        ChaosBehaviorStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new ChaosBehaviorStrategy(options), Resilion.Internal.StrategyType.Chaos);
    }
}
