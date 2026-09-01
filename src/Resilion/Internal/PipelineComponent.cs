namespace Resilion.Internal;

/// <summary>
/// Internal component in the pipeline execution chain. Each strategy is wrapped in a component
/// that calls the next component in the chain, forming a middleware-like pipeline.
/// </summary>
internal abstract class PipelineComponent : IDisposable
{
    /// <summary>
    /// Executes this component's logic and optionally delegates to the next component.
    /// </summary>
    internal abstract ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context);

    /// <summary>
    /// Executes this component's logic synchronously.
    /// </summary>
    internal abstract Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context);

    public virtual void Dispose()
    {
    }

    /// <summary>
    /// A no-op component that directly invokes the callback. Used as the terminal component.
    /// </summary>
    internal static PipelineComponent Empty { get; } = new EmptyComponent();

    private sealed class EmptyComponent : PipelineComponent
    {
        internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context)
            => callback(context);

        internal override Outcome<TResult> Execute<TResult>(
            Func<ResilienceContext, Outcome<TResult>> callback,
            ResilienceContext context)
            => callback(context);
    }
}

/// <summary>
/// Wraps a non-generic <see cref="Strategy"/> as a <see cref="PipelineComponent"/>.
/// </summary>
internal sealed class StrategyComponent : PipelineComponent
{
    private readonly Strategy _strategy;
    private readonly PipelineComponent _next;

    internal StrategyComponent(Strategy strategy, PipelineComponent next)
    {
        _strategy = strategy;
        _next = next;
    }

    internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        return _strategy.ExecuteAsync(
            ctx => _next.ExecuteAsync(callback, ctx),
            context);
    }

    internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        return _strategy.Execute(
            ctx => _next.Execute(callback, ctx),
            context);
    }

    public override void Dispose() => _strategy.Dispose();
}

/// <summary>
/// Wraps a generic <see cref="Strategy{TResult}"/> as a <see cref="PipelineComponent"/>.
/// When the execution result type does not match, the component passes through to the next component.
/// </summary>
internal sealed class TypedStrategyComponent<TStrategyResult> : PipelineComponent
{
    private readonly Strategy<TStrategyResult> _strategy;
    private readonly PipelineComponent _next;

    internal TypedStrategyComponent(Strategy<TStrategyResult> strategy, PipelineComponent next)
    {
        _strategy = strategy;
        _next = next;
    }

    internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        // Type check is a JIT-time constant for specific TResult instantiations.
        if (typeof(TResult) == typeof(TStrategyResult))
        {
            return ExecuteTypedAsync(callback, context);
        }

        // Type mismatch — skip this strategy entirely.
        return _next.ExecuteAsync(callback, context);
    }

    internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (typeof(TResult) == typeof(TStrategyResult))
        {
            return ExecuteTyped(callback, context);
        }

        return _next.Execute(callback, context);
    }

    private ValueTask<Outcome<TResult>> ExecuteTypedAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        // We know TResult == TStrategyResult at this point.
        // Reinterpret-cast the callback — zero allocation since Outcome<TResult> and
        // Outcome<TStrategyResult> have identical layout when TResult == TStrategyResult.
        var typedCallback = (Func<ResilienceContext, ValueTask<Outcome<TStrategyResult>>>)(object)callback;

        // Build the "next" callback that chains through _next then back to the user callback.
        var next = _next;
        var task = _strategy.ExecuteAsync(
            ctx => next.ExecuteAsync(typedCallback, ctx),
            context);

        return System.Runtime.CompilerServices.Unsafe.As<
            ValueTask<Outcome<TStrategyResult>>,
            ValueTask<Outcome<TResult>>>(ref task);
    }

    private Outcome<TResult> ExecuteTyped<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        var typedCallback = (Func<ResilienceContext, Outcome<TStrategyResult>>)(object)callback;

        var next = _next;
        var result = _strategy.Execute(
            ctx => next.Execute(typedCallback, ctx),
            context);

        return System.Runtime.CompilerServices.Unsafe.As<
            Outcome<TStrategyResult>,
            Outcome<TResult>>(ref result);
    }

    public override void Dispose() => _strategy.Dispose();
}
