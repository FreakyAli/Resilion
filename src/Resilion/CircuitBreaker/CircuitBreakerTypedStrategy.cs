namespace Resilion;

/// <summary>
/// Typed circuit breaker strategy — tracks failures by both exceptions and result values.
/// </summary>
internal sealed class CircuitBreakerTypedStrategy<TResult> : Strategy<TResult>
{
    private readonly CircuitBreakerStrategyOptions<TResult> _options;
    private readonly CircuitBreakerStateMachine _machine;

    internal CircuitBreakerTypedStrategy(CircuitBreakerStrategyOptions<TResult> options, TimeProvider timeProvider)
    {
        _options = options;
        _machine = new CircuitBreakerStateMachine(
            options.FailureRatioThreshold,
            options.MinimumThroughput,
            options.BreakDuration,
            options.BreakDurationGenerator,
            options.SamplingDuration,
            timeProvider,
            options.OnOpened,
            options.OnClosed,
            options.OnHalfOpened,
            options.ManualControl);
    }

    internal CircuitState State => _machine.State;

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        var rejection = _machine.TryReject(context);
        if (rejection is not null)
        {
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
            var isFailure = _options.ShouldHandleOutcome(outcome);
            await _machine.RecordOutcomeAsync(isFailure, context).ConfigureAwait(context.ContinueOnCapturedContext);
            return outcome;
        }
        catch (Exception ex)
        {
            var isFailure = _options.ShouldHandleOutcome(Outcome<TResult>.FromException(ex));
            await _machine.RecordOutcomeAsync(isFailure, context).ConfigureAwait(context.ContinueOnCapturedContext);
            throw;
        }
    }

    protected internal override Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        var rejection = _machine.TryReject(context);
        if (rejection is not null)
        {
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = callback(context);
            var isFailure = _options.ShouldHandleOutcome(outcome);
            _machine.RecordOutcome(isFailure, context);
            return outcome;
        }
        catch (Exception ex)
        {
            var isFailure = _options.ShouldHandleOutcome(Outcome<TResult>.FromException(ex));
            _machine.RecordOutcome(isFailure, context);
            throw;
        }
    }
}
