using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Owns the circuit breaker state machine — transitions, the sliding window, and event
/// dispatch — shared by both <see cref="CircuitBreakerStrategy"/> (exception-only) and
/// <see cref="CircuitBreakerTypedStrategy{TResult}"/> (result-based). Each strategy is
/// responsible only for evaluating its own failure predicate and reporting the resulting
/// <c>bool</c> to this machine; everything about *what a failure does* lives here exactly once.
/// </summary>
internal sealed class CircuitBreakerStateMachine
{
    private readonly double _failureRatioThreshold;
    private readonly int _minimumThroughput;
    private readonly TimeSpan _breakDuration;
    private readonly Func<BreakDurationGeneratorArgs, TimeSpan>? _breakDurationGenerator;
    private readonly ResilienceEventHandler<CircuitStateChangedEvent>? _onOpened;
    private readonly ResilienceEventHandler<CircuitStateChangedEvent>? _onClosed;
    private readonly ResilienceEventHandler<CircuitStateChangedEvent>? _onHalfOpened;
    private readonly TimeProvider _timeProvider;
    private readonly SlidingWindow _window;
    private readonly object _lock = new();

    private volatile CircuitState _state = CircuitState.Closed;
    private long _openedAtTimestamp;
    private int _halfOpenAttempts;
    private int _tripCount;
    private TimeSpan _effectiveBreakDuration;

    internal CircuitBreakerStateMachine(
        double failureRatioThreshold,
        int minimumThroughput,
        TimeSpan breakDuration,
        Func<BreakDurationGeneratorArgs, TimeSpan>? breakDurationGenerator,
        TimeSpan samplingDuration,
        TimeProvider timeProvider,
        ResilienceEventHandler<CircuitStateChangedEvent>? onOpened,
        ResilienceEventHandler<CircuitStateChangedEvent>? onClosed,
        ResilienceEventHandler<CircuitStateChangedEvent>? onHalfOpened,
        CircuitBreakerManualControl? manualControl)
    {
        _failureRatioThreshold = failureRatioThreshold;
        _minimumThroughput = minimumThroughput;
        _breakDuration = breakDuration;
        _breakDurationGenerator = breakDurationGenerator;
        _timeProvider = timeProvider;
        _onOpened = onOpened;
        _onClosed = onClosed;
        _onHalfOpened = onHalfOpened;
        _window = new SlidingWindow(samplingDuration, timeProvider);
        _effectiveBreakDuration = breakDuration;

        manualControl?.Initialize(
            onIsolate: () => { Isolate(); return Task.CompletedTask; },
            onReset: () => { Reset(); return Task.CompletedTask; });
    }

    internal CircuitState State => _state;

    /// <summary>
    /// Checks whether the circuit currently rejects calls, advancing Open → HalfOpen when the
    /// break duration has elapsed.
    /// </summary>
    internal CircuitBrokenException? TryReject(ResilienceContext context)
    {
        var state = _state;

        switch (state)
        {
            case CircuitState.Closed:
                return null;

            case CircuitState.HalfOpen:
                lock (_lock)
                {
                    if (_state != CircuitState.HalfOpen)
                    {
                        return _state is CircuitState.Open or CircuitState.Isolated
                            ? CreateRejectException()
                            : null;
                    }

                    if (_halfOpenAttempts > 0)
                    {
                        return CreateRejectException();
                    }

                    _halfOpenAttempts++;
                    return null;
                }

            case CircuitState.Open:
                var elapsed = _timeProvider.GetElapsedTime(_openedAtTimestamp);
                if (elapsed >= _effectiveBreakDuration)
                {
                    CircuitStateChangedEvent? pendingEvent = null;
                    lock (_lock)
                    {
                        if (_state == CircuitState.Open)
                        {
                            pendingEvent = TransitionTo(CircuitState.HalfOpen, context);
                            _halfOpenAttempts = 1;
                        }
                    }

                    FireEvent(pendingEvent);

                    return _state is CircuitState.Open or CircuitState.Isolated
                        ? CreateRejectException()
                        : null;
                }

                return CreateRejectException();

            case CircuitState.Isolated:
                return new CircuitBrokenException(CircuitState.Isolated, TimeSpan.Zero);

            default:
                return null;
        }
    }

    /// <summary>
    /// Records the outcome of a call (already classified as failure or not by the caller's
    /// predicate) and fires any resulting state-change event, synchronously.
    /// </summary>
    internal void RecordOutcome(bool isFailure, ResilienceContext context)
    {
        var pendingEvent = ComputeTransition(isFailure, context);
        if (pendingEvent.HasValue)
        {
            FireEvent(pendingEvent.Value);
        }
    }

    /// <summary>
    /// Records the outcome of a call and fires any resulting state-change event, asynchronously.
    /// </summary>
    internal async ValueTask RecordOutcomeAsync(bool isFailure, ResilienceContext context)
    {
        var pendingEvent = ComputeTransition(isFailure, context);
        if (pendingEvent.HasValue)
        {
            await FireEventAsync(pendingEvent.Value).ConfigureAwait(false);
        }
    }

    private CircuitStateChangedEvent? ComputeTransition(bool isFailure, ResilienceContext context)
    {
        var currentState = _state;

        switch (currentState)
        {
            case CircuitState.Closed:
                // Record outcome and get ratio under single lock for efficiency
                var failureRatio = _window.RecordAndGetRatio(isFailure, out var totalCount);
                if (failureRatio >= _failureRatioThreshold && totalCount >= _minimumThroughput)
                {
                    lock (_lock)
                    {
                        if (_state == CircuitState.Closed)
                        {
                            return Trip(context);
                        }
                    }
                }

                return null;

            case CircuitState.HalfOpen:
                lock (_lock)
                {
                    if (_state != CircuitState.HalfOpen)
                    {
                        return null;
                    }

                    if (isFailure)
                    {
                        return Trip(context);
                    }

                    _window.Reset();
                    return TransitionTo(CircuitState.Closed, context);
                }

            default:
                return null;
        }
    }

    private async ValueTask FireEventAsync(CircuitStateChangedEvent? evt)
    {
        if (evt is null)
        {
            return;
        }

        var e = evt.Value;
        ResilionTelemetry.CircuitBreakerStateChanges.Add(1, new(ResilionTelemetry.PipelineNameTag, e.Context.PipelineName), new(ResilionTelemetry.OperationKeyTag, e.Context.OperationKey));

        switch (e.CurrentState)
        {
            case CircuitState.Open:
                if (_onOpened is { } onOpened && onOpened.HasHandler)
                {
                    await onOpened.InvokeAsync(e).ConfigureAwait(false);
                }

                break;

            case CircuitState.Closed:
                if (_onClosed is { } onClosed && onClosed.HasHandler)
                {
                    await onClosed.InvokeAsync(e).ConfigureAwait(false);
                }

                break;

            case CircuitState.HalfOpen:
                if (_onHalfOpened is { } onHalfOpened && onHalfOpened.HasHandler)
                {
                    await onHalfOpened.InvokeAsync(e).ConfigureAwait(false);
                }

                break;
        }
    }

    /// <summary>Fires event callback outside lock. Emits telemetry.</summary>
    private void FireEvent(CircuitStateChangedEvent? evt)
    {
        if (evt is null)
        {
            return;
        }

        var e = evt.Value;
        ResilionTelemetry.CircuitBreakerStateChanges.Add(1, new(ResilionTelemetry.PipelineNameTag, e.Context.PipelineName), new(ResilionTelemetry.OperationKeyTag, e.Context.OperationKey));

        switch (e.CurrentState)
        {
            case CircuitState.Open:
                if (_onOpened is { } onOpened && onOpened.HasHandler)
                {
                    onOpened.Invoke(e);
                }

                break;

            case CircuitState.Closed:
                if (_onClosed is { } onClosed && onClosed.HasHandler)
                {
                    onClosed.Invoke(e);
                }

                break;

            case CircuitState.HalfOpen:
                if (_onHalfOpened is { } onHalfOpened && onHalfOpened.HasHandler)
                {
                    onHalfOpened.Invoke(e);
                }

                break;
        }
    }

    /// <summary>Must be called under lock.</summary>
    private CircuitStateChangedEvent? Trip(ResilienceContext context)
    {
        _openedAtTimestamp = _timeProvider.GetTimestamp();
        _halfOpenAttempts = 0;
        _tripCount++;
        _effectiveBreakDuration = GetEffectiveBreakDuration(context);
        return TransitionTo(CircuitState.Open, context);
    }

    /// <summary>Must be called under lock (called only from <see cref="Trip"/>).</summary>
    private TimeSpan GetEffectiveBreakDuration(ResilienceContext context)
    {
        if (_breakDurationGenerator is { } generator)
        {
            return generator(new BreakDurationGeneratorArgs(_tripCount, _breakDuration, context));
        }

        return _breakDuration;
    }

    /// <summary>Must be called under lock. Returns event to fire outside lock.</summary>
    private CircuitStateChangedEvent? TransitionTo(CircuitState newState, ResilienceContext context)
    {
        var previous = _state;
        _state = newState;
        return new CircuitStateChangedEvent(previous, newState, context);
    }

    private CircuitBrokenException CreateRejectException()
    {
        var remaining = TimeSpan.Zero;
        if (_state == CircuitState.Open)
        {
            var elapsed = _timeProvider.GetElapsedTime(_openedAtTimestamp);
            remaining = _effectiveBreakDuration - elapsed;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
        }

        return new CircuitBrokenException(_state, remaining);
    }

    private void Isolate()
    {
        lock (_lock)
        {
            _state = CircuitState.Isolated;
        }
    }

    private void Reset()
    {
        lock (_lock)
        {
            _window.Reset();
            _state = CircuitState.Closed;
            _halfOpenAttempts = 0;
            _tripCount = 0;
            _effectiveBreakDuration = _breakDuration;
        }
    }
}
