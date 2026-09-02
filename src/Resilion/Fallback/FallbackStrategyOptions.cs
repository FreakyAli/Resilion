namespace Resilion;

/// <summary>
/// Options for the Fallback resilience strategy.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <remarks>
/// Fallback is only available on typed pipelines (<see cref="Pipeline{TResult}"/>)
/// because it must produce a substitute value of the correct type.
/// </remarks>
public sealed record FallbackStrategyOptions<TResult>
{
    /// <summary>
    /// Gets the fallback action that produces a substitute result.
    /// Supports constant values, sync factories, and async factories via implicit conversion.
    /// </summary>
    public FallbackAction<TResult> FallbackAction { get; init; }

    /// <summary>
    /// Gets the predicate that determines which outcomes trigger the fallback.
    /// Defaults to all exceptions except <see cref="OperationCanceledException"/>.
    /// </summary>
    public Func<Outcome<TResult>, bool>? ShouldHandle { get; init; }

    /// <summary>
    /// Gets an optional event handler fired when the fallback is activated.
    /// </summary>
    public ResilienceEventHandler<OnFallbackEvent<TResult>>? OnFallback { get; init; }

    internal void Validate()
    {
        if (!FallbackAction.HasValue)
        {
            throw new InvalidOperationException(
                "FallbackAction must be configured. Assign a value, sync factory, or async factory.");
        }
    }

    internal bool ShouldHandleOutcome(Outcome<TResult> outcome)
    {
        if (ShouldHandle is not null)
        {
            return ShouldHandle(outcome);
        }

        return outcome.Exception is not null and not OperationCanceledException;
    }
}

/// <summary>
/// Event arguments for the <see cref="FallbackStrategyOptions{TResult}.OnFallback"/> callback.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="Outcome">The failed outcome that triggered the fallback.</param>
/// <param name="FallbackResult">The substitute result that will be returned.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct OnFallbackEvent<TResult>(
    Outcome<TResult> Outcome,
    TResult FallbackResult,
    ResilienceContext Context);
