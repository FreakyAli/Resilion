namespace Resilion.Extensions.Configuration;

/// <summary>A bindable description of <see cref="HedgingStrategyOptions{TResult}"/>.</summary>
/// <remarks>
/// Hedging options are always typed, so there is no non-generic projection.
/// <c>HedgingDelayGenerator</c>, <c>ActionGenerator</c>, <c>ShouldHandle</c> and <c>OnHedging</c>
/// are absent: all are delegates.
/// </remarks>
public sealed class HedgingStrategyConfiguration
{
    /// <summary>Gets or sets the attempt count, including the primary. Defaults to 2.</summary>
    public int MaxHedgedAttempts { get; set; } = 2;

    /// <summary>
    /// Gets or sets the delay before each hedged attempt. Defaults to 2 seconds.
    /// </summary>
    /// <remarks>
    /// This value selects the mode: <c>00:00:00</c> runs every attempt in parallel, a positive
    /// value waits that long unless an earlier attempt finishes first, and <c>-00:00:00.0010000</c>
    /// (<see cref="System.Threading.Timeout.InfiniteTimeSpan"/>) runs them sequentially.
    /// </remarks>
    public TimeSpan HedgingDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Projects this description into options, leaving all delegates unset.</summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public HedgingStrategyOptions<TResult> ToOptions<TResult>() => new()
    {
        MaxHedgedAttempts = MaxHedgedAttempts,
        HedgingDelay = HedgingDelay,
    };
}
