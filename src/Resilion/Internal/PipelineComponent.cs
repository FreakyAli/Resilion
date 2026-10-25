namespace Resilion.Internal;

/// <summary>
/// Internal component in the pipeline execution chain. Each strategy is wrapped in a component
/// that calls the next component in the chain, forming a middleware-like pipeline.
/// </summary>
internal abstract class PipelineComponent : IDisposable, IAsyncDisposable
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

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return default;
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

    public override void Dispose()
    {
        _strategy.Dispose();
        _next.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await _strategy.DisposeAsync().ConfigureAwait(false);
        await _next.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Wraps a generic <see cref="Strategy{TResult}"/> as a <see cref="PipelineComponent"/>.
/// </summary>
/// <remarks>
/// If the execution result type does not match <typeparamref name="TStrategyResult"/>, this throws
/// rather than skipping the strategy. A skipped strategy is a silent wrong answer — a retry that
/// never retries, or a circuit breaker that never breaks, on an execution that looks successful.
/// <para>
/// No public API can currently produce a mismatch: this component is only created by
/// <c>PipelineBuilder&lt;TResult&gt;.AddStrategy(Strategy&lt;TResult&gt;)</c>, which constrains the
/// strategy to the builder's result type, and that builder only yields a
/// <see cref="Pipeline{TResult}"/> whose execute methods instantiate exactly that type. The guard
/// exists so that a future composition surface cannot reintroduce a silent skip.
/// </para>
/// </remarks>
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

        throw TypeMismatch(typeof(TResult));
    }

    internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (typeof(TResult) == typeof(TStrategyResult))
        {
            return ExecuteTyped(callback, context);
        }

        throw TypeMismatch(typeof(TResult));
    }

    /// <summary>
    /// Builds the exception thrown when a typed strategy is reached by an execution whose result
    /// type differs from the strategy's. Previously this was a <c>Debug.WriteLine</c> and a
    /// pass-through; that warning was compiled out of Release builds by
    /// <c>[Conditional("DEBUG")]</c>, so shipped packages skipped the strategy with
    /// no diagnostic at all.
    /// </summary>
    private InvalidOperationException TypeMismatch(Type requestedType)
        => new(
            $"{_strategy.GetType().Name} is a Strategy<{typeof(TStrategyResult).Name}> but the pipeline " +
            $"was executed with result type {requestedType.Name}. A typed strategy cannot be applied to " +
            "an execution of a different result type. Use a Pipeline<" + typeof(TStrategyResult).Name +
            "> for this strategy, or add it as a non-generic Strategy.");

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

    public override void Dispose()
    {
        _strategy.Dispose();
        _next.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await _strategy.DisposeAsync().ConfigureAwait(false);
        await _next.DisposeAsync().ConfigureAwait(false);
    }
}
