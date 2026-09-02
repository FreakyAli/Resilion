using System.Runtime.ExceptionServices;
using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Executes operations with resilience. Strategies in the pipeline can react to exceptions
/// but do not inspect result values. Use this when you don't need result-based predicates
/// (e.g., timeout, rate limiting, exception-only retry).
/// </summary>
/// <remarks>
/// <para>
/// Pipelines are thread-safe and immutable after construction. Build once, cache, and reuse.
/// </para>
/// <para>
/// For pipelines that need to inspect return values (e.g., retry on HTTP 500, fallback),
/// use <see cref="Pipeline{TResult}"/>.
/// </para>
/// </remarks>
public sealed class Pipeline : IDisposable, IAsyncDisposable
{
    private readonly PipelineComponent _component;
    private readonly ResilienceContextPool _pool;
    private readonly string? _name;

    internal Pipeline(PipelineComponent component, ResilienceContextPool? pool = null, string? name = null)
    {
        _component = component;
        _pool = pool ?? ResilienceContextPool.Shared;
        _name = name;
    }

    /// <summary>
    /// Gets a no-op pipeline that passes through to the user delegate without applying any strategies.
    /// Useful for testing and conditional composition.
    /// </summary>
    public static Pipeline Empty { get; } = new(PipelineComponent.Empty);

    /// <summary>
    /// Gets the list of internal components for pipeline composition via <c>AddPipeline</c>.
    /// </summary>
    internal PipelineComponent Component => _component;

    /// <summary>
    /// Gets the optional name of this pipeline, used in telemetry and diagnostics.
    /// </summary>
    internal string? Name => _name;

    /// <summary>
    /// Creates a pipeline by configuring strategies on a builder.
    /// </summary>
    /// <param name="configure">A delegate that adds strategies to the builder.</param>
    /// <returns>A configured, immutable <see cref="Pipeline"/>.</returns>
    public static Pipeline Create(Action<PipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PipelineBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Creates a typed pipeline by configuring strategies on a builder.
    /// </summary>
    /// <typeparam name="TResult">The result type for the pipeline.</typeparam>
    /// <param name="configure">A delegate that adds strategies to the builder.</param>
    /// <returns>A configured, immutable <see cref="Pipeline{TResult}"/>.</returns>
    public static Pipeline<TResult> Create<TResult>(Action<PipelineBuilder<TResult>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PipelineBuilder<TResult>();
        configure(builder);
        return builder.Build();
    }

    // ──────────────────────────────────────────────────────────────────
    // Async execution
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an async operation through the pipeline with a state parameter to avoid closure allocations.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute. Use a <c>static</c> lambda with <paramref name="state"/> to avoid closure allocations.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public async ValueTask<TResult> ExecuteAsync<TResult, TState>(
        Func<TState, CancellationToken, ValueTask<TResult>> action,
        TState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var context = _pool.Rent(cancellationToken);
        try
        {
            // Set pipeline name in context for telemetry correlation
            context.PipelineName = _name;

            var outcome = await _component.ExecuteAsync<TResult>(
                async ctx =>
                {
                    try
                    {
                        var result = await action(state, ctx.CancellationToken).ConfigureAwait(ctx.ContinueOnCapturedContext);
                        return Outcome<TResult>.FromResult(result);
                    }
                    catch (Exception ex)
                    {
                        return Outcome<TResult>.FromException(ex);
                    }
                },
                context).ConfigureAwait(context.ContinueOnCapturedContext);

            return outcome.ThrowIfFailed();
        }
        finally
        {
            _pool.Return(context);
        }
    }

    /// <summary>
    /// Executes an async operation through the pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteAsync(
            static (act, ct) => act(ct),
            action,
            cancellationToken);
    }

    /// <summary>
    /// Executes an async void operation through the pipeline.
    /// </summary>
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var context = _pool.Rent(cancellationToken);
        try
        {
            // Set pipeline name in context for telemetry correlation
            context.PipelineName = _name;

            var outcome = await _component.ExecuteAsync<VoidResult>(
                async ctx =>
                {
                    try
                    {
                        await action(ctx.CancellationToken).ConfigureAwait(ctx.ContinueOnCapturedContext);
                        return Outcome<VoidResult>.FromResult(VoidResult.Instance);
                    }
                    catch (Exception ex)
                    {
                        return Outcome<VoidResult>.FromException(ex);
                    }
                },
                context).ConfigureAwait(context.ContinueOnCapturedContext);

            outcome.ThrowIfFailed();
        }
        finally
        {
            _pool.Return(context);
        }
    }

    /// <summary>
    /// Executes an async operation and returns the <see cref="Outcome{TResult}"/> without rethrowing.
    /// For advanced scenarios where you want to inspect the outcome without exception handling.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="context">The execution context (not pooled — caller manages lifetime).</param>
    /// <returns>The outcome of the operation.</returns>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TResult, TState>(
        Func<TState, ResilienceContext, ValueTask<Outcome<TResult>>> action,
        TState state,
        ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);

        return _component.ExecuteAsync(
            ctx => action(state, ctx),
            context);
    }

    // ──────────────────────────────────────────────────────────────────
    // Sync execution
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a synchronous operation through the pipeline with a state parameter to avoid closure allocations.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute. Use a <c>static</c> lambda with <paramref name="state"/> to avoid closure allocations.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public TResult Execute<TResult, TState>(
        Func<TState, CancellationToken, TResult> action,
        TState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var context = _pool.Rent(cancellationToken);
        context.IsSynchronous = true;
        // Set pipeline name in context for telemetry correlation
        context.PipelineName = _name;
        try
        {
            var outcome = _component.Execute<TResult>(
                ctx =>
                {
                    try
                    {
                        var result = action(state, ctx.CancellationToken);
                        return Outcome<TResult>.FromResult(result);
                    }
                    catch (Exception ex)
                    {
                        return Outcome<TResult>.FromException(ex);
                    }
                },
                context);

            return outcome.ThrowIfFailed();
        }
        finally
        {
            _pool.Return(context);
        }
    }

    /// <summary>
    /// Executes a synchronous operation through the pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public TResult Execute<TResult>(
        Func<CancellationToken, TResult> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Execute(static (act, ct) => act(ct), action, cancellationToken);
    }

    /// <summary>
    /// Executes a synchronous void operation through the pipeline.
    /// </summary>
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public void Execute(
        Action<CancellationToken> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        Execute(
            static (act, ct) =>
            {
                act(ct);
                return VoidResult.Instance;
            },
            action,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _component.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _component.DisposeAsync();
}

/// <summary>
/// Internal sentinel type for void pipeline executions.
/// </summary>
internal readonly struct VoidResult
{
    internal static readonly VoidResult Instance = default;
}
