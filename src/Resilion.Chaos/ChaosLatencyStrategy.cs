namespace Resilion.Chaos;

/// <summary>
/// Delays before invoking the rest of the pipeline.
/// </summary>
internal sealed class ChaosLatencyStrategy : Strategy
{
    private const string StrategyName = "latency";

    private readonly ChaosLatencyStrategyOptions _options;
    private readonly TimeProvider _timeProvider;

    internal ChaosLatencyStrategy(ChaosLatencyStrategyOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (TryResolveLatency(context, out var latency))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnLatencyInjected is { } handler)
            {
                await handler(new OnChaosLatencyArgs(context, latency)).ConfigureAwait(false);
            }

            // Through the builder's TimeProvider, so FakeTimeProvider drives this in tests.
            await Task.Delay(latency, _timeProvider, context.CancellationToken).ConfigureAwait(false);
        }

        return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (TryResolveLatency(context, out var latency))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnLatencyInjected is { } handler)
            {
                handler.InvokeSync(new OnChaosLatencyArgs(context, latency));
            }

            // Cancellation-aware sleep, matching the synchronous retry delay. This path does NOT
            // go through _timeProvider — WaitHandle has no TimeProvider equivalent — so a
            // FakeTimeProvider cannot drive it. Same limitation as sync retry delays; see
            // future-plans #9.
            context.CancellationToken.WaitHandle.WaitOne(latency);
            context.CancellationToken.ThrowIfCancellationRequested();
        }

        return callback(context);
    }

    private bool TryResolveLatency(ResilienceContext context, out TimeSpan latency)
    {
        latency = default;

        if (!ChaosDecision.ShouldInject(_options, context))
        {
            return false;
        }

        var resolved = _options.LatencyGenerator is { } generator
            ? generator(new ChaosGeneratorArgs(context))
            : _options.Latency;

        latency = resolved < TimeSpan.Zero ? TimeSpan.Zero : resolved;
        return true;
    }
}
