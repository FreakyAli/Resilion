namespace Resilion;

/// <summary>
/// A union type that holds either a synchronous <see cref="Action{T}"/> or an asynchronous
/// <see cref="Func{T, TResult}"/> returning <see cref="ValueTask"/>. Implicit conversions
/// let the compiler pick the right path automatically.
/// </summary>
/// <typeparam name="TArgs">The event arguments type (typically a readonly record struct).</typeparam>
/// <remarks>
/// <para>
/// This solves Polly v8's callback verbosity problem. Polly forces all callbacks to return
/// <c>ValueTask</c> even when they're synchronous (the 90% case — logging, incrementing counters).
/// </para>
/// <example>
/// <code>
/// // Sync — zero overhead, no ValueTask wrapping:
/// OnRetry = (RetryAttemptEvent e) => logger.Log(e.AttemptNumber)
///
/// // Async — when genuinely needed:
/// OnRetry = async (RetryAttemptEvent e) => await telemetry.TrackAsync(e)
/// </code>
/// </example>
/// </remarks>
public readonly struct ResilienceEventHandler<TArgs>
{
    private readonly Action<TArgs>? _syncHandler;
    private readonly Func<TArgs, ValueTask>? _asyncHandler;

    private ResilienceEventHandler(Action<TArgs>? syncHandler, Func<TArgs, ValueTask>? asyncHandler)
    {
        _syncHandler = syncHandler;
        _asyncHandler = asyncHandler;
    }

    /// <summary>
    /// Gets a value indicating whether this handler has been assigned.
    /// </summary>
    public bool HasHandler => _syncHandler is not null || _asyncHandler is not null;

    /// <summary>
    /// Invokes the handler. If the handler is synchronous, returns a completed <see cref="ValueTask"/>.
    /// </summary>
    /// <param name="args">The event arguments.</param>
    /// <returns>A <see cref="ValueTask"/> representing the completion of the handler.</returns>
    internal ValueTask InvokeAsync(TArgs args)
    {
        if (_syncHandler is not null)
        {
            _syncHandler(args);
            return default;
        }

        if (_asyncHandler is not null)
        {
            return _asyncHandler(args);
        }

        return default;
    }

    /// <summary>
    /// Invokes the handler synchronously. If the handler is async, blocks until completion.
    /// </summary>
    /// <param name="args">The event arguments.</param>
    internal void Invoke(TArgs args)
    {
        if (_syncHandler is not null)
        {
            _syncHandler(args);
            return;
        }

        if (_asyncHandler is not null)
        {
            // Copy to local to avoid struct 'this' capture in lambda.
            // Run on the thread pool to avoid deadlocking when a SynchronizationContext
            // is present (WPF, WinForms).
            var handler = _asyncHandler;
            Task.Run(() => handler(args).AsTask()).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Implicitly converts a synchronous <see cref="Action{T}"/> to a <see cref="ResilienceEventHandler{TArgs}"/>.
    /// </summary>
    public static implicit operator ResilienceEventHandler<TArgs>(Action<TArgs> handler)
        => new(handler, null);

    /// <summary>
    /// Implicitly converts an asynchronous <see cref="Func{T, TResult}"/> returning <see cref="ValueTask"/>
    /// to a <see cref="ResilienceEventHandler{TArgs}"/>.
    /// </summary>
    public static implicit operator ResilienceEventHandler<TArgs>(Func<TArgs, ValueTask> handler)
        => new(null, handler);
}
