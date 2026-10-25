namespace Resilion.Chaos;

/// <summary>
/// Options for the chaos outcome strategy, which substitutes a result (or a failure) instead of
/// calling inward.
/// </summary>
/// <typeparam name="TResult">The pipeline's result type.</typeparam>
/// <remarks>
/// This is the strategy for testing result-based predicates: a retry or circuit breaker
/// configured with <c>ShouldHandle</c> on the <em>value</em> only reacts if something produces
/// that value, and a fault injection never will.
/// </remarks>
public sealed record ChaosOutcomeStrategyOptions<TResult> : ChaosStrategyOptions
{
    /// <summary>Gets the outcome to inject.</summary>
    /// <remarks>Ignored when <see cref="OutcomeGenerator"/> is set.</remarks>
    public Outcome<TResult>? Outcome { get; init; }

    /// <summary>
    /// Gets a factory producing the outcome to inject, overriding <see cref="Outcome"/>.
    /// </summary>
    public Func<ChaosGeneratorArgs, Outcome<TResult>>? OutcomeGenerator { get; init; }

    /// <summary>Gets a handler invoked after an outcome is injected.</summary>
    public ResilienceEventHandler<OnChaosOutcomeArgs<TResult>>? OnOutcomeInjected { get; init; }

    internal void Validate()
    {
        ValidateShared();

        if (Outcome is null && OutcomeGenerator is null)
        {
            throw new InvalidOperationException(
                "A chaos outcome strategy needs something to return: set Outcome or OutcomeGenerator.");
        }
    }
}

/// <summary>Arguments for <see cref="ChaosOutcomeStrategyOptions{TResult}.OnOutcomeInjected"/>.</summary>
/// <typeparam name="TResult">The pipeline's result type.</typeparam>
/// <param name="Context">The execution context.</param>
/// <param name="Outcome">The outcome that was injected.</param>
public readonly record struct OnChaosOutcomeArgs<TResult>(ResilienceContext Context, Outcome<TResult> Outcome);
