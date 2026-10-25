namespace Resilion;

/// <summary>
/// A strategy event callback. Returns <see cref="ValueTask"/> so one property can accept both a
/// synchronous and an asynchronous handler.
/// </summary>
/// <typeparam name="TArgs">The event arguments type (typically a readonly record struct).</typeparam>
/// <remarks>
/// <para>
/// A synchronous handler returns <c>default</c> and costs nothing: a completed
/// <see cref="ValueTask"/> is a struct, so it allocates no more than an <see cref="Action{T}"/>
/// would. Only a handler that genuinely suspends allocates, and that is unavoidable.
/// </para>
/// <example>
/// <code>
/// // Synchronous — the common case.
/// OnRetry = e => { logger.LogWarning("Retry {Attempt}", e.AttemptNumber); return default; }
///
/// // Asynchronous.
/// OnRetry = async e => await telemetry.TrackAsync(e)
///
/// // A method group works too.
/// OnRetry = LogRetry;
/// </code>
/// </example>
/// <para>
/// This was a wrapper struct with implicit conversions from <see cref="Action{T}"/> and
/// <c>Func&lt;TArgs, ValueTask&gt;</c> before 1.0. It let a synchronous handler skip the
/// <c>return default</c>, but a struct is not a delegate type, so C# would not convert a bare
/// lambda (<c>CS1660</c>) or a method group (<c>CS0428</c>) to it — every callback had to be
/// written as <c>new Action&lt;TArgs&gt;(e =&gt; ...)</c>. The wrapper was also documented as
/// avoiding <c>ValueTask</c> wrapping; measurement showed both shapes allocate 0 B/op for a
/// synchronous handler, so it was buying syntax, not speed, and charging a compiler error for it.
/// </para>
/// </remarks>
/// <param name="args">The event arguments.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the handler is done.</returns>
public delegate ValueTask ResilienceEventHandler<TArgs>(TArgs args);

/// <summary>
/// Invocation helpers for <see cref="ResilienceEventHandler{TArgs}"/>.
/// </summary>
internal static class ResilienceEventHandlerExtensions
{
    /// <summary>
    /// Runs a handler to completion on the calling thread, for the synchronous execution paths.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A synchronous handler — one that returns <c>default</c> — completes inline here: no thread
    /// hop, no blocking, no allocation. That is the 90% case and it stays free.
    /// </para>
    /// <para>
    /// A handler that actually suspends has to be waited on, and waiting on the calling thread
    /// while a <see cref="SynchronizationContext"/> is installed is the classic deadlock: the
    /// handler's continuation is posted back to the very thread that is blocked waiting for it.
    /// The context is therefore cleared for the duration of the call, so any continuation inside
    /// the handler resumes on the thread pool instead and the wait can complete.
    /// </para>
    /// <para>
    /// Clearing beats the obvious alternative of invoking the handler inside
    /// <c>Task.Run</c>: that would push <em>every</em> handler onto the thread pool, including the
    /// synchronous ones that never needed to leave this thread.
    /// </para>
    /// </remarks>
    internal static void InvokeSync<TArgs>(this ResilienceEventHandler<TArgs> handler, TArgs args)
    {
        var previous = SynchronizationContext.Current;
        if (previous is null)
        {
            // Nothing to deadlock against.
            var direct = handler(args);
            if (direct.IsCompleted)
            {
                direct.GetAwaiter().GetResult();   // propagates a faulted handler; does not block
                return;
            }

            direct.AsTask().GetAwaiter().GetResult();
            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var task = handler(args);
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
                return;
            }

            // Safe to block: with no context captured, the continuation resumes on the pool.
            task.AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
