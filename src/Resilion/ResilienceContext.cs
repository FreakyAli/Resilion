namespace Resilion;

/// <summary>
/// Carries execution state through the resilience pipeline. Each pipeline execution
/// gets its own context, which provides the <see cref="CancellationToken"/>, operation metadata,
/// and a type-safe <see cref="Properties"/> bag for passing data between strategies.
/// </summary>
/// <remarks>
/// <para>
/// Contexts are pooled to avoid per-call allocations. Never construct a <see cref="ResilienceContext"/>
/// directly — use <see cref="ResilienceContextPool"/> to rent and return instances.
/// </para>
/// <para>
/// The pipeline's <c>ExecuteAsync</c> methods handle context pooling automatically.
/// Only use the pool directly when calling <c>ExecuteOutcomeAsync</c> with a custom context.
/// </para>
/// </remarks>
public sealed class ResilienceContext
{
    // Internal constructor — only the pool creates instances.
    internal ResilienceContext()
    {
    }

    /// <summary>
    /// Gets the cancellation token for this execution.
    /// Strategies may replace this with a linked token (e.g., Timeout strategy).
    /// </summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>
    /// Gets or sets an optional name identifying this operation, used for telemetry and diagnostics.
    /// </summary>
    /// <example>
    /// <code>
    /// var context = ResilienceContextPool.Shared.Rent(cancellationToken);
    /// context.OperationKey = "GetUserProfile";
    /// </code>
    /// </example>
    public string? OperationKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether async continuations should run on the captured
    /// synchronization context. Defaults to <c>false</c> (library-friendly behavior).
    /// </summary>
    /// <remarks>
    /// When <c>false</c>, all awaits inside the pipeline use <c>ConfigureAwait(false)</c>.
    /// Set to <c>true</c> when executing from a UI thread or a context that requires
    /// synchronization context flow.
    /// </remarks>
    public bool ContinueOnCapturedContext { get; set; }

    /// <summary>
    /// Gets the type-safe property bag for passing arbitrary data through the pipeline.
    /// </summary>
    public ResilienceProperties Properties { get; } = new();

    /// <summary>
    /// Gets a value indicating whether this execution is synchronous.
    /// </summary>
    internal bool IsSynchronous { get; set; }

    /// <summary>
    /// Resets this context to its initial state so it can be returned to the pool.
    /// </summary>
    internal void Reset()
    {
        CancellationToken = default;
        OperationKey = null;
        ContinueOnCapturedContext = false;
        IsSynchronous = false;
        Properties.Clear();
    }
}
