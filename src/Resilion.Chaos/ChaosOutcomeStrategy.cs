namespace Resilion.Chaos;

/// <summary>
/// Substitutes a configured outcome instead of invoking the rest of the pipeline.
/// </summary>
internal sealed class ChaosOutcomeStrategy<TResult> : Strategy<TResult>
{
    private const string StrategyName = "outcome";

    private readonly ChaosOutcomeStrategyOptions<TResult> _options;

    internal ChaosOutcomeStrategy(ChaosOutcomeStrategyOptions<TResult> options) => _options = options;

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (TryResolveOutcome(context, out var injected))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnOutcomeInjected is { } handler)
            {
                await handler(new OnChaosOutcomeArgs<TResult>(context, injected))
                    .ConfigureAwait(false);
            }

            return injected;
        }

        return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
    }

    protected internal override Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (TryResolveOutcome(context, out var injected))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnOutcomeInjected is { } handler)
            {
                handler.InvokeSync(new OnChaosOutcomeArgs<TResult>(context, injected));
            }

            return injected;
        }

        return callback(context);
    }

    private bool TryResolveOutcome(ResilienceContext context, out Outcome<TResult> outcome)
    {
        outcome = default;

        if (!ChaosDecision.ShouldInject(_options, context))
        {
            return false;
        }

        outcome = _options.OutcomeGenerator is { } generator
            ? generator(new ChaosGeneratorArgs(context))
            : _options.Outcome!.Value;

        return true;
    }
}
