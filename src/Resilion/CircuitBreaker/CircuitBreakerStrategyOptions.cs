using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Options for the Circuit Breaker resilience strategy (exception-only predicates).
/// </summary>
public sealed record CircuitBreakerStrategyOptions
{
    /// <summary>
    /// Gets the failure ratio threshold (0.0 to 1.0) that trips the circuit.
    /// Defaults to 0.5 (50%).
    /// </summary>
    public double FailureRatioThreshold { get; init; } = 0.5;

    /// <summary>
    /// Gets the sliding window duration for tracking successes and failures.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the minimum number of executions in the window before the circuit can trip.
    /// Prevents tripping on 1 failure out of 2 calls. Defaults to 10.
    /// </summary>
    public int MinimumThroughput { get; init; } = 10;

    /// <summary>
    /// Gets the duration the circuit stays open before transitioning to half-open.
    /// Defaults to 30 seconds. Ignored for a trip when <see cref="BreakDurationGenerator"/>
    /// is set — it's still passed to the generator as <see cref="BreakDurationGeneratorArgs.CurrentBreakDuration"/>.
    /// </summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets an optional generator that computes the break duration dynamically for each trip
    /// (e.g. exponential backoff on repeated trips). When set, it takes precedence over
    /// <see cref="BreakDuration"/>.
    /// </summary>
    public Func<BreakDurationGeneratorArgs, TimeSpan>? BreakDurationGenerator { get; init; }

    /// <summary>
    /// Gets the predicate that determines which exceptions count as failures.
    /// Defaults to all exceptions except <see cref="OperationCanceledException"/>.
    /// </summary>
    public Func<Exception, bool>? ShouldHandle { get; init; }

    /// <summary>
    /// Gets an optional event handler fired when the circuit transitions to open.
    /// </summary>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnOpened { get; init; }

    /// <summary>
    /// Gets an optional event handler fired when the circuit transitions to closed.
    /// </summary>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnClosed { get; init; }

    /// <summary>
    /// Gets an optional event handler fired when the circuit transitions to half-open.
    /// </summary>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnHalfOpened { get; init; }

    /// <summary>
    /// Gets an optional manual control for isolating and resetting the circuit.
    /// </summary>
    public CircuitBreakerManualControl? ManualControl { get; init; }

    internal void Validate()
    {
        if (FailureRatioThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(FailureRatioThreshold), FailureRatioThreshold,
                "FailureRatioThreshold must be between 0.0 and 1.0.");
        }

        if (SamplingDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingDuration), SamplingDuration,
                "SamplingDuration must be positive.");
        }

        if (MinimumThroughput < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumThroughput), MinimumThroughput,
                "MinimumThroughput must be >= 1.");
        }

        if (BreakDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BreakDuration), BreakDuration,
                "BreakDuration must be positive.");
        }
    }

    internal bool ShouldHandleException(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return ShouldHandle?.Invoke(exception) ?? false;
        }

        return ShouldHandle?.Invoke(exception) ?? true;
    }
}

/// <summary>
/// Options for the Circuit Breaker with result-based predicates.
/// </summary>
/// <typeparam name="TResult">The result type to inspect.</typeparam>
public sealed record CircuitBreakerStrategyOptions<TResult>
{
    /// <inheritdoc cref="CircuitBreakerStrategyOptions.FailureRatioThreshold"/>
    public double FailureRatioThreshold { get; init; } = 0.5;

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.SamplingDuration"/>
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.MinimumThroughput"/>
    public int MinimumThroughput { get; init; } = 10;

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.BreakDuration"/>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.BreakDurationGenerator"/>
    public Func<BreakDurationGeneratorArgs, TimeSpan>? BreakDurationGenerator { get; init; }

    /// <summary>
    /// Gets the predicate that determines which outcomes count as failures.
    /// </summary>
    public Func<Outcome<TResult>, bool>? ShouldHandle { get; init; }

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.OnOpened"/>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnOpened { get; init; }

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.OnClosed"/>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnClosed { get; init; }

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.OnHalfOpened"/>
    public ResilienceEventHandler<CircuitStateChangedEvent>? OnHalfOpened { get; init; }

    /// <inheritdoc cref="CircuitBreakerStrategyOptions.ManualControl"/>
    public CircuitBreakerManualControl? ManualControl { get; init; }

    internal void Validate()
    {
        if (FailureRatioThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(FailureRatioThreshold), FailureRatioThreshold,
                "FailureRatioThreshold must be between 0.0 and 1.0.");
        }

        if (SamplingDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingDuration), SamplingDuration,
                "SamplingDuration must be positive.");
        }

        if (MinimumThroughput < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumThroughput), MinimumThroughput,
                "MinimumThroughput must be >= 1.");
        }

        if (BreakDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BreakDuration), BreakDuration,
                "BreakDuration must be positive.");
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
/// Event arguments for circuit breaker state change events.
/// </summary>
/// <param name="PreviousState">The state before the transition.</param>
/// <param name="CurrentState">The state after the transition.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct CircuitStateChangedEvent(
    CircuitState PreviousState,
    CircuitState CurrentState,
    ResilienceContext Context);

/// <summary>
/// Arguments passed to a <c>BreakDurationGenerator</c> delegate when the circuit trips.
/// </summary>
/// <param name="FailureCount">How many times the circuit has tripped in total, including this trip.</param>
/// <param name="CurrentBreakDuration">The static <c>BreakDuration</c> configured on the options.</param>
/// <param name="Context">The execution context of the call that caused this trip.</param>
public readonly record struct BreakDurationGeneratorArgs(
    int FailureCount,
    TimeSpan CurrentBreakDuration,
    ResilienceContext Context);
