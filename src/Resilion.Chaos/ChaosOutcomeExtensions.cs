namespace Resilion.Chaos;

/// <summary>
/// Adds the chaos outcome strategy to a typed pipeline.
/// </summary>
/// <remarks>
/// <strong>There is deliberately no untyped overload.</strong> A <c>Strategy&lt;TResult&gt;</c>
/// added to a pipeline executing a different result type cannot run, so an untyped
/// <c>AddChaosOutcome</c> would be a trap with no correct use.
/// </remarks>
public static class ChaosOutcomeExtensions
{
    /// <summary>Adds a chaos outcome strategy.</summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="options">The outcome options.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder<TResult> AddChaosOutcome<TResult>(
        this PipelineBuilder<TResult> builder,
        ChaosOutcomeStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return builder.AddStrategy(new ChaosOutcomeStrategy<TResult>(options), Resilion.Internal.StrategyType.Chaos);
    }

    /// <summary>Adds a chaos outcome strategy with a rate and a result factory.</summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <param name="builder">The typed pipeline builder.</param>
    /// <param name="injectionRate">The probability of injecting, from 0.0 to 1.0.</param>
    /// <param name="resultFactory">Produces the result to return, called per injection.</param>
    /// <returns>The builder, for chaining.</returns>
    public static PipelineBuilder<TResult> AddChaosOutcome<TResult>(
        this PipelineBuilder<TResult> builder,
        double injectionRate,
        Func<TResult> resultFactory)
    {
        ArgumentNullException.ThrowIfNull(resultFactory);
        return builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<TResult>
        {
            InjectionRate = injectionRate,
            OutcomeGenerator = _ => Outcome<TResult>.FromResult(resultFactory()),
        });
    }
}
