namespace Resilion;

/// <summary>
/// Thrown when the circuit breaker is open or isolated and rejects an execution attempt.
/// </summary>
public sealed class CircuitBrokenException : ResilionException
{
    internal CircuitBrokenException(CircuitState state, TimeSpan retryAfter)
        : base(state == CircuitState.Isolated
            ? "The circuit breaker is manually isolated and rejecting all calls."
            : $"The circuit breaker is open and rejecting calls. Retry after {retryAfter.TotalSeconds:F1}s.")
    {
        CircuitState = state;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the state of the circuit breaker when the rejection occurred.
    /// </summary>
    public CircuitState CircuitState { get; }

    /// <summary>
    /// Gets the estimated duration until the circuit transitions to <see cref="CircuitState.HalfOpen"/>.
    /// Returns <see cref="TimeSpan.Zero"/> if the circuit is already half-open or isolated.
    /// </summary>
    public TimeSpan RetryAfter { get; }
}
