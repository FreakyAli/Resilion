using Resilion.Internal;

namespace Resilion;

/// <summary>
/// Executes operations returning <typeparamref name="TResult"/> with resilience. Strategies can react
/// to both exceptions AND specific result values (e.g., retry on HTTP 500, fallback to a default).
/// Required for Fallback, Hedging, and result-based predicates.
/// </summary>
/// <typeparam name="TResult">The result type of operations executed through this pipeline.</typeparam>
/// <remarks>
/// Pipelines are thread-safe and immutable after construction. Build once, cache, and reuse.
/// </remarks>
public sealed class Pipeline<TResult> : IDisposable, IAsyncDisposable
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
    /// </summary>
    public static Pipeline<TResult> Empty { get; } = new(PipelineComponent.Empty);

    /// <summary>
    /// Gets the internal component for pipeline composition.
    /// </summary>
    internal PipelineComponent Component => _component;

    /// <summary>
    /// Gets the optional name of this pipeline, used in telemetry and diagnostics.
    /// </summary>
    public string? Name => _name;

    // ──────────────────────────────────────────────────────────────────
    // Async execution
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an async operation through the pipeline with a state parameter to avoid closure allocations.
    /// </summary>
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute. Use a <c>static</c> lambda with <paramref name="state"/> to avoid closure allocations.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public async ValueTask<TResult> ExecuteAsync<TState>(
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
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public ValueTask<TResult> ExecuteAsync(
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteAsync(static (act, ct) => act(ct), action, cancellationToken);
    }

    /// <summary>
    /// Executes an async operation and returns the <see cref="Outcome{TResult}"/> without rethrowing.
    /// </summary>
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="context">The execution context (not pooled — caller manages lifetime).</param>
    /// <returns>The outcome of the operation.</returns>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TState>(
        Func<TState, ResilienceContext, ValueTask<Outcome<TResult>>> action,
        TState state,
        ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        context.PipelineName = _name;

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
    /// <typeparam name="TState">The type of the state passed to the callback.</typeparam>
    /// <param name="action">The operation to execute.</param>
    /// <param name="state">State passed to the callback.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public TResult Execute<TState>(
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
    /// <param name="action">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    public TResult Execute(
        Func<CancellationToken, TResult> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Execute(static (act, ct) => act(ct), action, cancellationToken);
    }

    private int _disposed;

    /// <inheritdoc />
    /// <remarks>
    /// Idempotent: the second and later calls are no-ops. A pipeline can legitimately be reached
    /// by two owners — the DI container disposes a keyed singleton, and
    /// <c>ResiliencePipelineRegistry</c> disposes every pipeline it created — so without this
    /// guard the same instance is disposed twice. That is harmless while no strategy holds a
    /// resource, and a real bug the day one does.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _component.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Idempotent, for the same reason as <see cref="Dispose"/>.</remarks>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _component.DisposeAsync();
    }
}
