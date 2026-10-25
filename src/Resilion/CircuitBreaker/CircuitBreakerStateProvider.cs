namespace Resilion;

/// <summary>
/// Exposes the current <see cref="CircuitState"/> of the circuit breaker it is bound to.
/// </summary>
/// <remarks>
/// Pass an instance via <c>CircuitBreakerStrategyOptions.StateProvider</c> and read
/// <see cref="State"/> afterwards. Without this there is no way to observe a circuit's state:
/// <see cref="CircuitBreakerManualControl"/> can isolate and reset a circuit but cannot report it,
/// which forced callers into indirect assertions such as "the next call threw
/// <c>CircuitBrokenException</c>, therefore it must be open".
/// <para>
/// One provider binds to one circuit breaker, matching
/// <see cref="CircuitBreakerManualControl"/>'s behaviour: reusing an instance across two breakers
/// would silently report one of them, so the second binding throws.
/// </para>
/// <para>
/// Intended for observation — health endpoints, dashboards, logging, and tests. See
/// <see cref="State"/> for why it must not gate execution.
/// </para>
/// </remarks>
public sealed class CircuitBreakerStateProvider
{
    private readonly object _initLock = new();
    private Func<CircuitState>? _getState;

    /// <summary>
    /// Gets the circuit breaker's state at the moment of the read.
    /// </summary>
    /// <remarks>
    /// <strong>Do not use this to decide whether to execute.</strong> It is a point-in-time read of
    /// a value other threads change concurrently, so anything you conclude from it may already be
    /// stale by the next line:
    /// <code>
    /// // WRONG — the circuit can open between the check and the call, and this also
    /// // bypasses the half-open probe that lets the circuit recover.
    /// if (provider.State == CircuitState.Closed)
    /// {
    ///     await pipeline.ExecuteAsync(CallDependencyAsync);
    /// }
    ///
    /// // RIGHT — execute and let the strategy reject. It holds the lock that makes the
    /// // decision correct, and an open circuit fails fast on its own.
    /// try
    /// {
    ///     await pipeline.ExecuteAsync(CallDependencyAsync);
    /// }
    /// catch (CircuitBrokenException)
    /// {
    ///     // shed load, serve cached data, surface a 503 — whatever degradation suits
    /// }
    /// </code>
    /// The guard is not merely racy, it is counterproductive: skipping the call while the circuit
    /// is open also skips the trial call that transitions it to half-open, so a circuit gated this
    /// way can stay open after the dependency has recovered.
    /// <para>
    /// Reading it for a health check, a metric, a log line, or a test assertion is exactly what it
    /// is for.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This provider is not associated with any circuit breaker strategy.
    /// </exception>
    public CircuitState State
    {
        get
        {
            var getState = _getState
                ?? throw new InvalidOperationException(
                    "This CircuitBreakerStateProvider is not associated with a circuit breaker " +
                    "strategy. Pass it via CircuitBreakerStrategyOptions.StateProvider when " +
                    "building the pipeline.");

            return getState();
        }
    }

    internal void Initialize(Func<CircuitState> getState)
    {
        lock (_initLock)
        {
            if (_getState is not null)
            {
                throw new InvalidOperationException(
                    "This CircuitBreakerStateProvider is already bound to a circuit breaker " +
                    "strategy. Create a separate instance for each circuit breaker.");
            }

            _getState = getState;
        }
    }
}
