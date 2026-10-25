using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Resilion.RateLimiting;

/// <summary>
/// Rate limiter strategy — acquires a lease before execution, rejects if the limit is exceeded.
/// </summary>
internal sealed class RateLimiterStrategy : Strategy
{
    private readonly RateLimiterStrategyOptions _options;

    internal RateLimiterStrategy(RateLimiterStrategyOptions options)
    {
        _options = options;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        using var activity = StrategyActivity.Start("RateLimiter", context);

        var lease = await _options.RateLimiter!.AcquireAsync(
            permitCount: 1,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        try
        {
            if (!lease.IsAcquired)
            {
                var result = await HandleRejection<TResult>(lease, context).ConfigureAwait(false);
                StrategyActivity.SetOutcome(activity, "rejected");
                return result;
            }

            var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
            StrategyActivity.SetOutcome(activity, outcome.IsSuccess ? "success" : "failure");
            return outcome;
        }
        finally
        {
            lease.Dispose();
        }
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        using var activity = StrategyActivity.Start("RateLimiter", context);

        // Sync acquire — AttemptAcquire does not wait in a queue.
        using var lease = _options.RateLimiter!.AttemptAcquire(permitCount: 1);

        if (!lease.IsAcquired)
        {
            var result = HandleRejectionSync<TResult>(lease, context);
            StrategyActivity.SetOutcome(activity, "rejected");
            return result;
        }

        var outcome = callback(context);
        StrategyActivity.SetOutcome(activity, outcome.IsSuccess ? "success" : "failure");
        return outcome;
    }

    private async ValueTask<Outcome<TResult>> HandleRejection<TResult>(
        RateLimitLease lease,
        ResilienceContext context)
    {
        var retryAfter = GetRetryAfter(lease);

        Resilion.ResilionTelemetry.RateLimiterRejections.Add(1, new(Resilion.ResilionTelemetry.PipelineNameTag, context.PipelineName), new(Resilion.ResilionTelemetry.OperationKeyTag, context.OperationKey));

        if (_options.OnRejected is { } handler && handler.HasHandler)
        {
            await handler.InvokeAsync(new OnRateLimitRejectedEvent(retryAfter, context))
                .ConfigureAwait(false);
        }

        return Outcome<TResult>.FromException(new RateLimitRejectedException(retryAfter));
    }

    private Outcome<TResult> HandleRejectionSync<TResult>(
        RateLimitLease lease,
        ResilienceContext context)
    {
        var retryAfter = GetRetryAfter(lease);

        Resilion.ResilionTelemetry.RateLimiterRejections.Add(1, new(Resilion.ResilionTelemetry.PipelineNameTag, context.PipelineName), new(Resilion.ResilionTelemetry.OperationKeyTag, context.OperationKey));

        if (_options.OnRejected is { } handler && handler.HasHandler)
        {
            handler.Invoke(new OnRateLimitRejectedEvent(retryAfter, context));
        }

        return Outcome<TResult>.FromException(new RateLimitRejectedException(retryAfter));
    }

    private static TimeSpan? GetRetryAfter(RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            return retryAfter;
        }

        return null;
    }
}
