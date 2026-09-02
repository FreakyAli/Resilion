namespace Resilion;

/// <summary>
/// The current state of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Normal operation. Requests flow through and failures are tracked.
    /// </summary>
    Closed,

    /// <summary>
    /// The circuit is tripped. All requests are immediately rejected
    /// with <see cref="CircuitBrokenException"/>.
    /// </summary>
    Open,

    /// <summary>
    /// A limited number of probe requests are allowed through to test recovery.
    /// Success transitions to <see cref="Closed"/>; failure transitions back to <see cref="Open"/>.
    /// </summary>
    HalfOpen,

    /// <summary>
    /// Manually isolated via <see cref="CircuitBreakerManualControl.IsolateAsync"/>.
    /// All requests are rejected until <see cref="CircuitBreakerManualControl.ResetAsync"/> is called.
    /// </summary>
    Isolated,
}
