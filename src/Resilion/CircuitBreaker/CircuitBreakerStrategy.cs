using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Non-generic circuit breaker strategy — tracks failures by exception only.
/// </summary>
internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly CircuitBreakerStrategyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SlidingWindow _window;
    private readonly object _lock = new();

    private volatile CircuitState _state = CircuitState.Closed;
    private long _openedAtTimestamp;
    private int _halfOpenAttempts;

    internal CircuitBreakerStrategy(CircuitBreakerStrategyOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _window = new SlidingWindow(options.SamplingDuration, timeProvider);

        options.ManualControl?.Initialize(
            onIsolate: () => { Isolate(); return Task.CompletedTask; },
            onReset: () => { Reset(); return Task.CompletedTask; });
    }

    internal CircuitState State => _state;

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        var rejection = TryReject(context);
        if (rejection is not null)
        {
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
            await RecordOutcomeAsync(outcome.Exception, context).ConfigureAwait(context.ContinueOnCapturedContext);
            return outcome;
        }
        catch (Exception ex)
        {
            await RecordOutcomeAsync(ex, context).ConfigureAwait(context.ContinueOnCapturedContext);
            throw;
        }
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        var rejection = TryReject(context);
        if (rejection is not null)
        {
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = callback(context);
            RecordOutcome(outcome.Exception, context);
            return outcome;
        }
        catch (Exception ex)
        {
            RecordOutcome(ex, context);
            throw;
        }
    }

    private CircuitBrokenException? TryReject(ResilienceContext context)
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
                if (elapsed >= _options.BreakDuration)
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

    private void RecordOutcome(Exception? exception, ResilienceContext context)
    {
        var isFailure = exception is not null && _options.ShouldHandleException(exception);
        RecordAndTransition(isFailure, context);
    }

    private async ValueTask RecordOutcomeAsync(Exception? exception, ResilienceContext context)
    {
        var isFailure = exception is not null && _options.ShouldHandleException(exception);
        await RecordAndTransitionAsync(isFailure, context).ConfigureAwait(false);
    }

    private async ValueTask RecordAndTransitionAsync(bool isFailure, ResilienceContext context)
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
                if (failureRatio >= _options.FailureRatioThreshold
                    && totalCount >= _options.MinimumThroughput)
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

        ResilionTelemetry.CircuitBreakerStateChanges.Add(1);

        var e = evt.Value;
        switch (e.CurrentState)
        {
            case CircuitState.Open:
                if (_options.OnOpened is { } onOpened && onOpened.HasHandler)
                {
                    await onOpened.InvokeAsync(e).ConfigureAwait(false);
                }

                break;

            case CircuitState.Closed:
                if (_options.OnClosed is { } onClosed && onClosed.HasHandler)
                {
                    await onClosed.InvokeAsync(e).ConfigureAwait(false);
                }

                break;

            case CircuitState.HalfOpen:
                if (_options.OnHalfOpened is { } onHalfOpened && onHalfOpened.HasHandler)
                {
                    await onHalfOpened.InvokeAsync(e).ConfigureAwait(false);
                }

                break;
        }
    }

    private void RecordAndTransition(bool isFailure, ResilienceContext context)
    {
        var pendingEvent = ComputeTransition(isFailure, context);
        if (pendingEvent.HasValue)
        {
            FireEvent(pendingEvent.Value);
        }
    }

    /// <summary>Must be called under lock.</summary>
    private CircuitStateChangedEvent? Trip(ResilienceContext context)
    {
        _openedAtTimestamp = _timeProvider.GetTimestamp();
        _halfOpenAttempts = 0;
        return TransitionTo(CircuitState.Open, context);
    }

    /// <summary>Must be called under lock. Returns event to fire outside lock.</summary>
    private CircuitStateChangedEvent? TransitionTo(CircuitState newState, ResilienceContext context)
    {
        var previous = _state;
        _state = newState;
        return new CircuitStateChangedEvent(previous, newState, context);
    }

    /// <summary>Fires event callback outside lock. Emits telemetry.</summary>
    private void FireEvent(CircuitStateChangedEvent? evt)
    {
        if (evt is null)
        {
            return;
        }

        ResilionTelemetry.CircuitBreakerStateChanges.Add(1);

        var e = evt.Value;
        switch (e.CurrentState)
        {
            case CircuitState.Open:
                if (_options.OnOpened is { } onOpened && onOpened.HasHandler)
                {
                    onOpened.Invoke(e);
                }

                break;

            case CircuitState.Closed:
                if (_options.OnClosed is { } onClosed && onClosed.HasHandler)
                {
                    onClosed.Invoke(e);
                }

                break;

            case CircuitState.HalfOpen:
                if (_options.OnHalfOpened is { } onHalfOpened && onHalfOpened.HasHandler)
                {
                    onHalfOpened.Invoke(e);
                }

                break;
        }
    }

    private CircuitBrokenException CreateRejectException()
    {
        var remaining = TimeSpan.Zero;
        if (_state == CircuitState.Open)
        {
            var elapsed = _timeProvider.GetElapsedTime(_openedAtTimestamp);
            remaining = _options.BreakDuration - elapsed;
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
        }
    }
}
