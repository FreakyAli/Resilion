namespace Resilion.Chaos;

/// <summary>
/// Throws a configured exception instead of invoking the rest of the pipeline.
/// </summary>
/// <remarks>
/// Derives from <see cref="Strategy"/> rather than <see cref="Strategy{TResult}"/> because a
/// thrown fault is result-agnostic: one instance works for a pipeline of any result type.
/// </remarks>
internal sealed class ChaosFaultStrategy : Strategy
{
    private const string StrategyName = "fault";

    private readonly ChaosFaultStrategyOptions _options;

    internal ChaosFaultStrategy(ChaosFaultStrategyOptions options) => _options = options;

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (TryResolveFault(context, out var fault))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnFaultInjected is { } handler)
            {
                await handler(new OnChaosFaultArgs(context, fault)).ConfigureAwait(false);
            }

            return Outcome<TResult>.FromException(fault);
        }

        return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (TryResolveFault(context, out var fault))
        {
            ChaosDecision.RecordInjection(StrategyName, context);

            if (_options.OnFaultInjected is { } handler)
            {
                handler.InvokeSync(new OnChaosFaultArgs(context, fault));
            }

            return Outcome<TResult>.FromException(fault);
        }

        return callback(context);
    }

    /// <summary>
    /// Decides whether to inject and, if so, what to throw. A generator returning null means
    /// "not this time" and passes through — the injection decision is then spent without effect,
    /// which is deliberate: it lets a generator filter on the context.
    /// </summary>
    private bool TryResolveFault(ResilienceContext context, out Exception fault)
    {
        fault = null!;

        if (!ChaosDecision.ShouldInject(_options, context))
        {
            return false;
        }

        var resolved = _options.FaultGenerator is { } generator
            ? generator(new ChaosGeneratorArgs(context))
            : _options.Fault;

        if (resolved is null)
        {
            return false;
        }

        fault = resolved;
        return true;
    }
}
