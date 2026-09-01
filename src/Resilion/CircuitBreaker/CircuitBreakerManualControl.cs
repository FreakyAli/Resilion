namespace Resilion;

/// <summary>
/// Provides manual control over a circuit breaker's state.
/// Use <see cref="IsolateAsync"/> to force the circuit open and <see cref="ResetAsync"/> to close it.
/// </summary>
/// <remarks>
/// Create an instance and pass it via <see cref="CircuitBreakerStrategyOptions.ManualControl"/>.
/// The control must be associated with a strategy before it can be used.
/// </remarks>
public sealed class CircuitBreakerManualControl
{
    private Func<Task>? _onIsolate;
    private Func<Task>? _onReset;

    /// <summary>
    /// Forces the circuit into the <see cref="CircuitState.Isolated"/> state.
    /// All subsequent calls will be rejected until <see cref="ResetAsync"/> is called.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this control is not associated with any circuit breaker strategy.
    /// </exception>
    public Task IsolateAsync()
    {
        if (_onIsolate is null)
        {
            throw new InvalidOperationException(
                "This CircuitBreakerManualControl is not associated with a circuit breaker strategy. " +
                "Pass it via CircuitBreakerStrategyOptions.ManualControl when building the pipeline.");
        }

        return _onIsolate();
    }

    /// <summary>
    /// Resets the circuit to the <see cref="CircuitState.Closed"/> state and clears the sliding window.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this control is not associated with any circuit breaker strategy.
    /// </exception>
    public Task ResetAsync()
    {
        if (_onReset is null)
        {
            throw new InvalidOperationException(
                "This CircuitBreakerManualControl is not associated with a circuit breaker strategy. " +
                "Pass it via CircuitBreakerStrategyOptions.ManualControl when building the pipeline.");
        }

        return _onReset();
    }

    internal void Initialize(Func<Task> onIsolate, Func<Task> onReset)
    {
        if (Interlocked.CompareExchange(ref _onIsolate, onIsolate, null) is not null)
        {
            throw new InvalidOperationException(
                "This CircuitBreakerManualControl is already bound to a circuit breaker strategy. " +
                "Create a separate instance for each circuit breaker.");
        }

        _onReset = onReset;
    }
}
