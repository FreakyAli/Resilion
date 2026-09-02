using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Builds a <see cref="Pipeline"/> by composing resilience strategies. Strategies execute
/// outermost to innermost — the first strategy added is the first to see each call.
/// </summary>
/// <remarks>
/// <para>
/// The canonical strategy order is: RateLimiter → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout.
/// </para>
/// <para>
/// Builders are not thread-safe and should not be shared. Build once, then cache and reuse
/// the resulting <see cref="Pipeline"/>.
/// </para>
/// </remarks>
public sealed class PipelineBuilder
{
    private readonly List<Func<PipelineComponent, PipelineComponent>> _componentFactories = [];
    private readonly List<StrategyType> _strategyTypes = [];

    /// <summary>
    /// Gets or sets an optional name for the pipeline, used in telemetry and diagnostics.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="System.TimeProvider"/> used by time-dependent strategies.
    /// Defaults to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// When <c>true</c>, suppresses ordering validation warnings at <see cref="Build"/> time.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool SuppressOrderingWarnings { get; set; }

    /// <summary>
    /// Optional callback that receives ordering validation warnings at <see cref="Build"/> time.
    /// When null, warnings are written to <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// </summary>
    public Action<string>? OnValidationWarning { get; set; }

    /// <summary>
    /// Adds a non-generic strategy to the pipeline.
    /// </summary>
    /// <param name="strategy">The strategy to add.</param>
    /// <returns>This builder for chaining.</returns>
    public PipelineBuilder AddStrategy(Strategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new StrategyComponent(strategy, next));
        _strategyTypes.Add(StrategyType.Unknown);
        return this;
    }

    internal PipelineBuilder AddStrategy(Strategy strategy, StrategyType type)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new StrategyComponent(strategy, next));
        _strategyTypes.Add(type);
        return this;
    }

    /// <summary>
    /// Flattens another pipeline's strategies into this builder, enabling composition of pre-built pipelines.
    /// </summary>
    /// <param name="pipeline">The pipeline whose strategies will be added.</param>
    /// <returns>This builder for chaining.</returns>
    public PipelineBuilder AddPipeline(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var outerComponent = pipeline.Component;
        _componentFactories.Add(next => new DelegatingComponent(outerComponent, next));
        _strategyTypes.Add(StrategyType.Custom);
        return this;
    }

    /// <summary>
    /// Builds the pipeline. The builder should not be used after calling this method.
    /// </summary>
    /// <returns>An immutable, thread-safe <see cref="Pipeline"/>.</returns>
    public Pipeline Build()
    {
        if (_componentFactories.Count == 0)
        {
            return Pipeline.Empty;
        }

        // Validate ordering unless suppressed.
        if (!SuppressOrderingWarnings && _strategyTypes.Count >= 2)
        {
            var warnings = OrderingValidator.Validate(_strategyTypes);
            EmitWarnings(warnings);
        }

        var component = PipelineComponent.Empty;
        for (var i = _componentFactories.Count - 1; i >= 0; i--)
        {
            component = _componentFactories[i](component);
        }

        return new Pipeline(component);
    }

    private void EmitWarnings(List<string> warnings)
    {
        foreach (var warning in warnings)
        {
            if (OnValidationWarning is not null)
            {
                OnValidationWarning(warning);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Resilion] Ordering warning: {warning}");
            }
        }
    }
}

/// <summary>
/// Builds a <see cref="Pipeline{TResult}"/> by composing resilience strategies. Supports both
/// non-generic strategies (Timeout, RateLimiter) and typed strategies (Fallback, Hedging).
/// </summary>
/// <typeparam name="TResult">The result type for the pipeline.</typeparam>
public sealed class PipelineBuilder<TResult>
{
    private readonly List<Func<PipelineComponent, PipelineComponent>> _componentFactories = [];
    private readonly List<StrategyType> _strategyTypes = [];

    /// <summary>
    /// Gets or sets an optional name for the pipeline, used in telemetry and diagnostics.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="System.TimeProvider"/> used by time-dependent strategies.
    /// Defaults to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// When <c>true</c>, suppresses ordering validation warnings at <see cref="Build"/> time.
    /// </summary>
    public bool SuppressOrderingWarnings { get; set; }

    /// <summary>
    /// Optional callback that receives ordering validation warnings.
    /// When null, warnings go to <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// </summary>
    public Action<string>? OnValidationWarning { get; set; }

    /// <summary>
    /// Adds a non-generic strategy (e.g., Timeout, RateLimiter) to the typed pipeline.
    /// </summary>
    public PipelineBuilder<TResult> AddStrategy(Strategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new StrategyComponent(strategy, next));
        _strategyTypes.Add(StrategyType.Unknown);
        return this;
    }

    internal PipelineBuilder<TResult> AddStrategy(Strategy strategy, StrategyType type)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new StrategyComponent(strategy, next));
        _strategyTypes.Add(type);
        return this;
    }

    /// <summary>
    /// Adds a typed strategy (e.g., Fallback, result-based Retry) to the pipeline.
    /// </summary>
    public PipelineBuilder<TResult> AddStrategy(Strategy<TResult> strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new TypedStrategyComponent<TResult>(strategy, next));
        _strategyTypes.Add(StrategyType.Unknown);
        return this;
    }

    internal PipelineBuilder<TResult> AddStrategy(Strategy<TResult> strategy, StrategyType type)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _componentFactories.Add(next => new TypedStrategyComponent<TResult>(strategy, next));
        _strategyTypes.Add(type);
        return this;
    }

    /// <summary>
    /// Adds a delegate-based strategy for quick inline resilience logic.
    /// </summary>
    public PipelineBuilder<TResult> AddStrategy(
        string name,
        Func<ResilienceContext, Func<ResilienceContext, ValueTask<Outcome<TResult>>>, ValueTask<Outcome<TResult>>> handler)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(handler);
        _componentFactories.Add(next =>
            new TypedStrategyComponent<TResult>(new DelegateStrategy<TResult>(name, handler), next));
        _strategyTypes.Add(StrategyType.Custom);
        return this;
    }

    /// <summary>
    /// Flattens another typed pipeline's strategies into this builder.
    /// </summary>
    public PipelineBuilder<TResult> AddPipeline(Pipeline<TResult> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var outerComponent = pipeline.Component;
        _componentFactories.Add(next => new DelegatingComponent(outerComponent, next));
        _strategyTypes.Add(StrategyType.Custom);
        return this;
    }

    /// <summary>
    /// Flattens a non-generic pipeline's strategies into this typed builder.
    /// </summary>
    public PipelineBuilder<TResult> AddPipeline(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var outerComponent = pipeline.Component;
        _componentFactories.Add(next => new DelegatingComponent(outerComponent, next));
        _strategyTypes.Add(StrategyType.Custom);
        return this;
    }

    /// <summary>
    /// Builds the pipeline.
    /// </summary>
    public Pipeline<TResult> Build()
    {
        if (_componentFactories.Count == 0)
        {
            return Pipeline<TResult>.Empty;
        }

        if (!SuppressOrderingWarnings && _strategyTypes.Count >= 2)
        {
            var warnings = OrderingValidator.Validate(_strategyTypes);
            EmitWarnings(warnings);
        }

        var component = PipelineComponent.Empty;
        for (var i = _componentFactories.Count - 1; i >= 0; i--)
        {
            component = _componentFactories[i](component);
        }

        return new Pipeline<TResult>(component);
    }

    private void EmitWarnings(List<string> warnings)
    {
        foreach (var warning in warnings)
        {
            if (OnValidationWarning is not null)
            {
                OnValidationWarning(warning);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Resilion] Ordering warning: {warning}");
            }
        }
    }
}

/// <summary>
/// Internal strategy wrapper for delegate-based strategies on typed pipelines.
/// </summary>
internal sealed class DelegateStrategy<TResult> : Strategy<TResult>
{
    private readonly string _name;
    private readonly Func<ResilienceContext, Func<ResilienceContext, ValueTask<Outcome<TResult>>>, ValueTask<Outcome<TResult>>> _handler;

    internal DelegateStrategy(
        string name,
        Func<ResilienceContext, Func<ResilienceContext, ValueTask<Outcome<TResult>>>, ValueTask<Outcome<TResult>>> handler)
    {
        _name = name;
        _handler = handler;
    }

    protected internal override ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
        => _handler(context, callback);

    public override string ToString() => $"DelegateStrategy<{typeof(TResult).Name}>({_name})";
}

/// <summary>
/// Internal component that delegates to an existing pipeline's component chain,
/// then continues to the next component in the current chain.
/// </summary>
internal sealed class DelegatingComponent : PipelineComponent
{
    private readonly PipelineComponent _inner;
    private readonly PipelineComponent _next;

    internal DelegatingComponent(PipelineComponent inner, PipelineComponent next)
    {
        _inner = inner;
        _next = next;
    }

    internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        return _inner.ExecuteAsync(
            ctx => _next.ExecuteAsync(callback, ctx),
            context);
    }

    internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        return _inner.Execute(
            ctx => _next.Execute(callback, ctx),
            context);
    }

    public override void Dispose()
    {
        // Don't dispose inner — it may be shared via AddPipeline.
        // Don't dispose next — the parent chain owns it.
    }
}
