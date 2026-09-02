using System.Diagnostics;

namespace Resilion;

/// <summary>
/// Typed fallback strategy — provides a substitute result when the operation fails.
/// </summary>
internal sealed class FallbackStrategy<TResult> : Strategy<TResult>
{
    private readonly FallbackStrategyOptions<TResult> _options;

    internal FallbackStrategy(FallbackStrategyOptions<TResult> options)
    {
        _options = options;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("Fallback");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "Fallback");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);

        if (!_options.ShouldHandleOutcome(outcome))
        {
            if (activity is not null)
            {
                activity.SetTag("outcome", outcome.IsSuccess ? "success" : "failure");
            }
            return outcome;
        }

        ResilionTelemetry.FallbackActivations.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

        var fallbackCtx = new FallbackContext<TResult>(outcome, context);
        var fallbackResult = await _options.FallbackAction.ExecuteAsync(fallbackCtx).ConfigureAwait(false);

        if (_options.OnFallback is { } handler && handler.HasHandler)
        {
            await handler.InvokeAsync(new OnFallbackEvent<TResult>(outcome, fallbackResult, context))
                .ConfigureAwait(false);
        }

        if (activity is not null)
        {
            activity.SetTag("outcome", "fallback_applied");
        }

        return Outcome<TResult>.FromResult(fallbackResult);
    }

    protected internal override Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("Fallback");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "Fallback");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        var outcome = callback(context);

        if (!_options.ShouldHandleOutcome(outcome))
        {
            if (activity is not null)
            {
                activity.SetTag("outcome", outcome.IsSuccess ? "success" : "failure");
            }
            return outcome;
        }

        ResilionTelemetry.FallbackActivations.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

        var fallbackCtx = new FallbackContext<TResult>(outcome, context);
        var fallbackResult = _options.FallbackAction.Execute(fallbackCtx);

        if (_options.OnFallback is { } handler && handler.HasHandler)
        {
            handler.Invoke(new OnFallbackEvent<TResult>(outcome, fallbackResult, context));
        }

        if (activity is not null)
        {
            activity.SetTag("outcome", "fallback_applied");
        }

        return Outcome<TResult>.FromResult(fallbackResult);
    }
}
