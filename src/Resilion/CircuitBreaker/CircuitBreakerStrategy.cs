using System.Diagnostics;

namespace Resilion;

/// <summary>
/// Non-generic circuit breaker strategy — tracks failures by exception only.
/// </summary>
internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly CircuitBreakerStrategyOptions _options;
    private readonly CircuitBreakerStateMachine _machine;

    internal CircuitBreakerStrategy(CircuitBreakerStrategyOptions options, TimeProvider timeProvider)
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

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("CircuitBreaker");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "CircuitBreaker");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        var rejection = _machine.TryReject(context);
        if (rejection is not null)
        {
            if (activity is not null)
            {
                activity.SetTag("outcome", "rejected");
            }
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
            await _machine.RecordOutcomeAsync(IsFailure(outcome.Exception), context)
                .ConfigureAwait(context.ContinueOnCapturedContext);
            if (activity is not null)
            {
                activity.SetTag("outcome", outcome.IsSuccess ? "success" : "failure");
            }
            return outcome;
        }
        catch (Exception ex)
        {
            await _machine.RecordOutcomeAsync(IsFailure(ex), context).ConfigureAwait(context.ContinueOnCapturedContext);
            if (activity is not null)
            {
                activity.SetTag("outcome", "exception");
            }
            throw;
        }
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("CircuitBreaker");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "CircuitBreaker");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        var rejection = _machine.TryReject(context);
        if (rejection is not null)
        {
            if (activity is not null)
            {
                activity.SetTag("outcome", "rejected");
            }
            return Outcome<TResult>.FromException(rejection);
        }

        try
        {
            var outcome = callback(context);
            _machine.RecordOutcome(IsFailure(outcome.Exception), context);
            if (activity is not null)
            {
                activity.SetTag("outcome", outcome.IsSuccess ? "success" : "failure");
            }
            return outcome;
        }
        catch (Exception ex)
        {
            _machine.RecordOutcome(IsFailure(ex), context);
            if (activity is not null)
            {
                activity.SetTag("outcome", "exception");
            }
            throw;
        }
    }

    private bool IsFailure(Exception? exception)
        => exception is not null && _options.ShouldHandleException(exception);
}
