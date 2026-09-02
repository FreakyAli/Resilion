namespace Resilion;

/// <summary>
/// Options for the Timeout resilience strategy.
/// </summary>
/// <remarks>
/// <para>
/// Timeout relies on cooperative cancellation. The operation must observe the
/// <see cref="CancellationToken"/> passed through the execution context. If the operation
/// ignores the token, the timeout has no effect — the strategy cannot forcibly abort it.
/// </para>
/// <para>
/// To apply a per-attempt timeout inside retry, place the Timeout strategy <em>after</em>
/// the Retry strategy in the pipeline. To apply a total timeout across all retries,
/// place it <em>before</em> the Retry strategy.
/// </para>
/// </remarks>
public sealed record TimeoutStrategyOptions
{
    /// <summary>
    /// Gets the timeout duration. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// <para>Set to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the timeout (passthrough).</para>
    /// <para>Set to <see cref="TimeSpan.Zero"/> to immediately timeout (useful for testing).</para>
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets an optional delegate that computes the timeout duration dynamically per execution.
    /// When set, <see cref="Timeout"/> is ignored.
    /// </summary>
    public Func<TimeoutGeneratorArgs, TimeSpan>? TimeoutGenerator { get; init; }

    /// <summary>
    /// Gets an optional event handler that fires when a timeout occurs.
    /// </summary>
    public ResilienceEventHandler<OnTimeoutArgs>? OnTimeout { get; init; }

    internal void Validate()
    {
        if (TimeoutGenerator is null && Timeout < TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                "Timeout must be a non-negative TimeSpan, TimeSpan.Zero, or Timeout.InfiniteTimeSpan.");
        }
    }
}

/// <summary>
/// Arguments passed to the <see cref="TimeoutStrategyOptions.TimeoutGenerator"/> delegate.
/// </summary>
/// <param name="Context">The execution context for the current operation.</param>
public readonly record struct TimeoutGeneratorArgs(ResilienceContext Context);

/// <summary>
/// Arguments passed to the <see cref="TimeoutStrategyOptions.OnTimeout"/> event handler.
/// </summary>
/// <param name="Context">The execution context for the timed-out operation.</param>
/// <param name="Timeout">The timeout duration that was applied.</param>
/// <param name="ElapsedTime">The actual elapsed time before timeout was triggered.</param>
public readonly record struct OnTimeoutArgs(
    ResilienceContext Context,
    TimeSpan Timeout,
    TimeSpan ElapsedTime);
