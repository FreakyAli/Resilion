namespace Resilion.Chaos;

/// <summary>
/// Runs a configured payload before invoking the rest of the pipeline.
/// </summary>
internal sealed class ChaosBehaviorStrategy : Strategy
{
    private const string StrategyName = "behavior";

    private readonly ChaosBehaviorStrategyOptions _options;

    internal ChaosBehaviorStrategy(ChaosBehaviorStrategyOptions options) => _options = options;

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (ChaosDecision.ShouldInject(_options, context))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            // Not wrapped in try/catch: a behavior that throws is a fault the pipeline should
            // see, which is the whole point of injecting it.
            await _options.Behavior!(new ChaosGeneratorArgs(context)).ConfigureAwait(context.ContinueOnCapturedContext);

            if (_options.OnBehaviorInjected is { } handler)
            {
                await handler(new OnChaosBehaviorArgs(context)).ConfigureAwait(context.ContinueOnCapturedContext);
            }
        }

        return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (ChaosDecision.ShouldInject(_options, context))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            // InvokeSync, not Behavior.Invoke: the latter is the delegate's own Invoke, which
            // fires the payload and silently discards the returned ValueTask, so an async
            // behavior would never be awaited on this path.
            _options.Behavior!.InvokeSync(new ChaosGeneratorArgs(context));

            if (_options.OnBehaviorInjected is { } handler)
            {
                handler.InvokeSync(new OnChaosBehaviorArgs(context));
            }
        }

        return callback(context);
    }
}
