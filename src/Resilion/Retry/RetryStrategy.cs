using System.Diagnostics;

namespace Resilion;

/// <summary>
/// Non-generic retry strategy — retries on exceptions only.
/// </summary>
internal sealed class RetryStrategy : Strategy
{
    private readonly RetryStrategyOptions _options;
    private readonly TimeProvider _timeProvider;

    internal RetryStrategy(RetryStrategyOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("Retry");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "Retry");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        if (_options.MaxRetryAttempts == 0)
        {
            return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
        }

        Outcome<TResult> outcome = default;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            // Check cancellation before each attempt.
            context.CancellationToken.ThrowIfCancellationRequested();

            outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);

            // Success — return immediately.
            if (outcome.IsSuccess)
            {
                if (activity is not null)
                {
                    activity.SetTag("outcome", "success");
                }
                return outcome;
            }

            // Check if this exception should trigger a retry.
            if (!_options.ShouldHandleException(outcome.Exception!))
            {
                if (activity is not null)
                {
                    activity.SetTag("outcome", "failure");
                }
                return outcome;
            }

            // Last attempt — don't wait, just return the failure.
            if (attempt == _options.MaxRetryAttempts)
            {
                break;
            }

            // Compute delay.
            var retryNumber = attempt + 1;
            var delay = _options.Delay.ComputeDelay(retryNumber, _options.UseJitter);
            if (_options.MaxDelay.HasValue && delay > _options.MaxDelay.Value)
            {
                delay = _options.MaxDelay.Value;
            }

            ResilionTelemetry.RetryAttempts.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

            // Fire OnRetry callback.
            if (_options.OnRetry is { } handler && handler.HasHandler)
            {
                await handler.InvokeAsync(
                    new RetryAttemptEvent(retryNumber, delay, outcome.Exception!, context))
                    .ConfigureAwait(false);
            }

            // Wait for the delay (if any), respecting cancellation.
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, context.CancellationToken)
                    .ConfigureAwait(context.ContinueOnCapturedContext);
            }
        }

        if (activity is not null)
        {
            activity.SetTag("outcome", "retry_exhausted");
        }

        return outcome;
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        using var activity = ResilionTelemetry.ActivitySource.StartActivity("Retry");
        if (activity is not null)
        {
            activity.SetTag("strategy.name", "Retry");
            activity.SetTag("pipeline.name", context.PipelineName);
            activity.SetTag("operation.key", context.OperationKey);
        }

        if (_options.MaxRetryAttempts == 0)
        {
            return callback(context);
        }

        Outcome<TResult> outcome = default;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            outcome = callback(context);

            if (outcome.IsSuccess)
            {
                if (activity is not null)
                {
                    activity.SetTag("outcome", "success");
                }
                return outcome;
            }

            if (!_options.ShouldHandleException(outcome.Exception!))
            {
                if (activity is not null)
                {
                    activity.SetTag("outcome", "failure");
                }
                return outcome;
            }

            if (attempt == _options.MaxRetryAttempts)
            {
                break;
            }

            var retryNumber = attempt + 1;
            var delay = _options.Delay.ComputeDelay(retryNumber, _options.UseJitter);
            if (_options.MaxDelay.HasValue && delay > _options.MaxDelay.Value)
            {
                delay = _options.MaxDelay.Value;
            }

            ResilionTelemetry.RetryAttempts.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

            if (_options.OnRetry is { } handler && handler.HasHandler)
            {
                handler.Invoke(new RetryAttemptEvent(retryNumber, delay, outcome.Exception!, context));
            }

            if (delay > TimeSpan.Zero)
            {
                // Sync path: use WaitHandle for cancellation-aware sleep.
                context.CancellationToken.WaitHandle.WaitOne(delay);
                context.CancellationToken.ThrowIfCancellationRequested();
            }
        }

        if (activity is not null)
        {
            activity.SetTag("outcome", "retry_exhausted");
        }

        return outcome;
    }
}

/// <summary>
/// Generic retry strategy — retries on exceptions and/or result values.
/// </summary>
internal sealed class RetryStrategy<TResult> : Strategy<TResult>
{
    private readonly RetryStrategyOptions<TResult> _options;
    private readonly TimeProvider _timeProvider;

    internal RetryStrategy(RetryStrategyOptions<TResult> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (_options.MaxRetryAttempts == 0)
        {
            return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
        }

        Outcome<TResult> outcome = default;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);

            // Check predicate against the full outcome (exception OR result).
            if (!_options.ShouldHandleOutcome(outcome))
            {
                return outcome;
            }

            if (attempt == _options.MaxRetryAttempts)
            {
                break;
            }

            var retryNumber = attempt + 1;
            var delay = _options.Delay.ComputeDelay(retryNumber, _options.UseJitter);
            if (_options.MaxDelay.HasValue && delay > _options.MaxDelay.Value)
            {
                delay = _options.MaxDelay.Value;
            }

            ResilionTelemetry.RetryAttempts.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

            if (_options.OnRetry is { } handler && handler.HasHandler)
            {
                await handler.InvokeAsync(
                    new RetryAttemptEvent<TResult>(retryNumber, delay, outcome, context))
                    .ConfigureAwait(false);
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, context.CancellationToken)
                    .ConfigureAwait(context.ContinueOnCapturedContext);
            }
        }

        return outcome;
    }

    protected internal override Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (_options.MaxRetryAttempts == 0)
        {
            return callback(context);
        }

        Outcome<TResult> outcome = default;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            outcome = callback(context);

            if (!_options.ShouldHandleOutcome(outcome))
            {
                return outcome;
            }

            if (attempt == _options.MaxRetryAttempts)
            {
                break;
            }

            var retryNumber = attempt + 1;
            var delay = _options.Delay.ComputeDelay(retryNumber, _options.UseJitter);
            if (_options.MaxDelay.HasValue && delay > _options.MaxDelay.Value)
            {
                delay = _options.MaxDelay.Value;
            }

            ResilionTelemetry.RetryAttempts.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

            if (_options.OnRetry is { } handler && handler.HasHandler)
            {
                handler.Invoke(new RetryAttemptEvent<TResult>(retryNumber, delay, outcome, context));
            }

            if (delay > TimeSpan.Zero)
            {
                context.CancellationToken.WaitHandle.WaitOne(delay);
                context.CancellationToken.ThrowIfCancellationRequested();
            }
        }

        return outcome;
    }
}
