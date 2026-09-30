using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Options for the Hedging resilience strategy.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <remarks>
/// <para>
/// Hedging reduces tail latency by racing multiple concurrent attempts.
/// The first successful attempt wins; all others are cancelled.
/// </para>
/// <para>
/// Three modes based on <see cref="HedgingDelay"/>:
/// <list type="bullet">
/// <item><b>Latency mode</b> (delay &gt; 0): Wait, then fire secondary if primary is still pending</item>
/// <item><b>Parallel mode</b> (delay = 0): Fire all attempts simultaneously</item>
/// <item><b>Sequential mode</b> (<see cref="System.Threading.Timeout.InfiniteTimeSpan"/>): Wait for failure before trying next</item>
/// </list>
/// </para>
/// </remarks>
public sealed record HedgingStrategyOptions<TResult>
{
    /// <summary>
    /// Gets the maximum number of hedged attempts, including the primary.
    /// Defaults to 2 (one primary + one hedged).
    /// </summary>
    public int MaxHedgedAttempts { get; init; } = 2;

    /// <summary>
    /// Gets the delay before launching each additional hedged attempt.
    /// Defaults to 2 seconds. Set to <see cref="TimeSpan.Zero"/> for parallel mode.
    /// Set to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for sequential mode.
    /// </summary>
    public TimeSpan HedgingDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets an optional generator computing the delay before each hedged attempt. When set,
    /// <see cref="HedgingDelay"/> is ignored.
    /// </summary>
    /// <remarks>
    /// Because the delay selects the hedging mode, a generator selects it <em>per attempt</em>:
    /// returning <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> makes that attempt wait
    /// for its predecessor, <see cref="TimeSpan.Zero"/> launches it immediately, and any positive
    /// value waits that long unless an earlier attempt finishes first. That is the point of the
    /// feature — for example, hedge the first retry eagerly and later ones only on real latency.
    /// <para>
    /// A negative result other than <c>InfiniteTimeSpan</c> is clamped to
    /// <see cref="TimeSpan.Zero"/> rather than throwing, because failing mid-execution would turn
    /// a delay-computation slip into a lost request.
    /// </para>
    /// <para>
    /// Not supported on the synchronous path: <c>Execute</c> throws when this is set, for the same
    /// reason it rejects parallel and latency modes.
    /// </para>
    /// </remarks>
    public Func<HedgingDelayGeneratorArgs, TimeSpan>? HedgingDelayGenerator { get; init; }

    /// <summary>
    /// Gets the predicate that determines which outcomes should trigger hedging.
    /// Defaults to all exceptions except <see cref="OperationCanceledException"/>.
    /// </summary>
    public Func<Outcome<TResult>, bool>? ShouldHandle { get; init; }

    /// <summary>
    /// Gets an optional action generator for hedged attempts. When <c>null</c>, the original
    /// action is re-executed. When set, can return different actions per attempt
    /// (e.g., call a different endpoint).
    /// </summary>
    public Func<HedgingActionContext, Func<CancellationToken, ValueTask<TResult>>?>? ActionGenerator { get; init; }

    /// <summary>
    /// Gets an optional event handler fired before each hedged attempt is launched.
    /// </summary>
    public ResilienceEventHandler<OnHedgingEvent<TResult>>? OnHedging { get; init; }

    internal void Validate()
    {
        if (MaxHedgedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHedgedAttempts), MaxHedgedAttempts,
                "MaxHedgedAttempts must be >= 1.");
        }

        // Skip the static range check when a generator is present: HedgingDelay is then ignored,
        // so rejecting its value would refuse a configuration that is entirely consistent.
        if (HedgingDelayGenerator is null
            && HedgingDelay < TimeSpan.Zero
            && HedgingDelay != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(HedgingDelay), HedgingDelay,
                "HedgingDelay must be non-negative, TimeSpan.Zero, or Timeout.InfiniteTimeSpan.");
        }
    }

    internal bool ShouldHandleOutcome(Outcome<TResult> outcome)
    {
        if (ShouldHandle is not null)
        {
            return ShouldHandle(outcome);
        }

        return OutcomePredicates.DefaultShouldHandle(outcome);
    }
}

/// <summary>
/// Context passed to the <see cref="HedgingStrategyOptions{TResult}.ActionGenerator"/> delegate.
/// </summary>
/// <param name="AttemptNumber">The 0-based attempt index (0 = primary, 1 = first hedge, etc.).</param>
public readonly record struct HedgingActionContext(int AttemptNumber);

/// <summary>
/// Arguments passed to <see cref="HedgingStrategyOptions{TResult}.HedgingDelayGenerator"/>.
/// </summary>
/// <param name="AttemptNumber">
/// The 0-based index of the attempt about to be launched, so <c>1</c> for the first hedge.
/// Matches <see cref="HedgingActionContext.AttemptNumber"/> and
/// <c>OnHedgingEvent&lt;TResult&gt;.AttemptNumber</c>.
/// </param>
/// <param name="Context">The resilience context for the execution.</param>
public readonly record struct HedgingDelayGeneratorArgs(int AttemptNumber, ResilienceContext Context);

/// <summary>
/// Event arguments for the <see cref="HedgingStrategyOptions{TResult}.OnHedging"/> callback.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="AttemptNumber">The 0-based attempt index being launched.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct OnHedgingEvent<TResult>(
    int AttemptNumber,
    ResilienceContext Context);
