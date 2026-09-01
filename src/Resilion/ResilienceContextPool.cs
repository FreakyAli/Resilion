using System.Collections.Concurrent;

namespace Resilion;

/// <summary>
/// Provides pooled <see cref="ResilienceContext"/> instances to avoid per-call allocations.
/// </summary>
/// <remarks>
/// The pipeline's <c>ExecuteAsync</c> methods use this pool automatically. Only use it directly
/// when you need a custom context for <c>ExecuteOutcomeAsync</c>.
/// </remarks>
public sealed class ResilienceContextPool
{
    // Thread-safe bag for pooled contexts. ConcurrentBag is well-suited here because
    // contexts are rented and returned from the same thread most of the time (LIFO affinity).
    private readonly ConcurrentBag<ResilienceContext> _pool = new();

    // Cap the pool size to avoid unbounded memory growth under burst traffic.
    // 256 is generous — most applications use far fewer concurrent pipeline executions.
    private const int MaxPoolSize = 256;

    /// <summary>
    /// Gets the shared default pool instance. Use this unless you have a specific reason
    /// to create a separate pool.
    /// </summary>
    public static ResilienceContextPool Shared { get; } = new();

    /// <summary>
    /// Rents a <see cref="ResilienceContext"/> from the pool with the specified cancellation token.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for this execution.</param>
    /// <returns>A pooled or newly created <see cref="ResilienceContext"/>.</returns>
    public ResilienceContext Rent(CancellationToken cancellationToken = default)
    {
        if (!_pool.TryTake(out var context))
        {
            context = new ResilienceContext();
        }

        context.CancellationToken = cancellationToken;
        return context;
    }

    /// <summary>
    /// Returns a <see cref="ResilienceContext"/> to the pool. The context is reset to its
    /// initial state before being pooled.
    /// </summary>
    /// <param name="context">The context to return.</param>
    public void Return(ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Reset();

        // Don't let the pool grow without bound.
        if (_pool.Count < MaxPoolSize)
        {
            _pool.Add(context);
        }
    }
}
