namespace Resilion;

/// <summary>
/// Base class for proactive resilience strategies that work with any result type.
/// Timeout, RateLimiter, and exception-only Retry/CircuitBreaker inherit from this.
/// </summary>
/// <remarks>
/// <para>
/// The key distinction: <see cref="Strategy"/> is non-generic at the <em>class</em> level
/// but generic at the <em>method</em> level. A single Timeout instance can protect calls
/// returning <c>string</c>, <c>int</c>, <c>HttpResponseMessage</c>, or any other type.
/// </para>
/// <para>
/// For strategies that need to inspect or substitute result values (Fallback, Hedging,
/// result-based Retry), use <see cref="Strategy{TResult}"/> instead.
/// </para>
/// </remarks>
public abstract class Strategy : IDisposable
{
    /// <summary>
    /// Executes the resilience logic around the given callback.
    /// </summary>
    /// <typeparam name="TResult">The result type of the operation.</typeparam>
    /// <param name="callback">The next strategy or user delegate to invoke.</param>
    /// <param name="context">The execution context carrying cancellation and properties.</param>
    /// <returns>The outcome of the execution.</returns>
    protected internal abstract ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context);

    /// <summary>
    /// Executes the resilience logic synchronously around the given callback.
    /// </summary>
    /// <typeparam name="TResult">The result type of the operation.</typeparam>
    /// <param name="callback">The next strategy or user delegate to invoke.</param>
    /// <param name="context">The execution context carrying cancellation and properties.</param>
    /// <returns>The outcome of the execution.</returns>
    protected internal virtual Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        // Default: delegate to async path synchronously.
        // Strategies that support true sync execution should override this method.
        return ExecuteAsync<TResult>(
            ctx => new ValueTask<Outcome<TResult>>(callback(ctx)),
            context).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
    }
}

/// <summary>
/// Base class for reactive resilience strategies bound to a specific result type <typeparamref name="TResult"/>.
/// Fallback, Hedging, and result-based Retry/CircuitBreaker inherit from this.
/// </summary>
/// <typeparam name="TResult">The result type this strategy operates on.</typeparam>
/// <remarks>
/// <para>
/// The key distinction: <see cref="Strategy{TResult}"/> fixes the result type at the <em>class</em> level.
/// A <c>Strategy&lt;HttpResponseMessage&gt;</c> can inspect status codes and substitute fallback responses.
/// </para>
/// <para>
/// For strategies that don't need to inspect results (Timeout, RateLimiter), use <see cref="Strategy"/> instead.
/// </para>
/// </remarks>
public abstract class Strategy<TResult> : IDisposable
{
    /// <summary>
    /// Executes the resilience logic around the given callback.
    /// </summary>
    /// <param name="callback">The next strategy or user delegate to invoke.</param>
    /// <param name="context">The execution context carrying cancellation and properties.</param>
    /// <returns>The outcome of the execution.</returns>
    protected internal abstract ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context);

    /// <summary>
    /// Executes the resilience logic synchronously around the given callback.
    /// </summary>
    /// <param name="callback">The next strategy or user delegate to invoke.</param>
    /// <param name="context">The execution context carrying cancellation and properties.</param>
    /// <returns>The outcome of the execution.</returns>
    protected internal virtual Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        // Default: delegate to async path synchronously.
        // Strategies that support true sync execution should override this method.
        return ExecuteAsync(
            ctx => new ValueTask<Outcome<TResult>>(callback(ctx)),
            context).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
    }
}
