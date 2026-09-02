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

        if (HedgingDelay < TimeSpan.Zero && HedgingDelay != System.Threading.Timeout.InfiniteTimeSpan)
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

        return outcome.Exception is not null and not OperationCanceledException;
    }
}

/// <summary>
/// Context passed to the <see cref="HedgingStrategyOptions{TResult}.ActionGenerator"/> delegate.
/// </summary>
/// <param name="AttemptNumber">The 0-based attempt index (0 = primary, 1 = first hedge, etc.).</param>
public readonly record struct HedgingActionContext(int AttemptNumber);

/// <summary>
/// Event arguments for the <see cref="HedgingStrategyOptions{TResult}.OnHedging"/> callback.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="AttemptNumber">The 0-based attempt index being launched.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct OnHedgingEvent<TResult>(
    int AttemptNumber,
    ResilienceContext Context);
