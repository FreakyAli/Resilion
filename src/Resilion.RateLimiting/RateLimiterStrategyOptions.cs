using System.Threading.RateLimiting;

namespace Resilion.RateLimiting;

/// <summary>
/// Options for the Rate Limiter resilience strategy.
/// </summary>
/// <remarks>
/// <para>
/// The strategy wraps <see cref="System.Threading.RateLimiting.RateLimiter"/> — it does not
/// reimplement rate limiting. Configure the limiter using the .NET built-in algorithms:
/// <see cref="FixedWindowRateLimiter"/>, <see cref="SlidingWindowRateLimiter"/>,
/// <see cref="TokenBucketRateLimiter"/>, or <see cref="ConcurrencyLimiter"/>.
/// </para>
/// <para>
/// The strategy does NOT own the <see cref="RateLimiter"/>'s lifetime. The caller (or DI container)
/// is responsible for disposing it.
/// </para>
/// </remarks>
public sealed record RateLimiterStrategyOptions
{
    /// <summary>
    /// Gets the rate limiter instance. Required — there is no default.
    /// </summary>
    public RateLimiter? RateLimiter { get; init; }

    /// <summary>
    /// Gets an optional event handler fired when a request is rejected by the rate limiter.
    /// </summary>
    public ResilienceEventHandler<OnRateLimitRejectedEvent>? OnRejected { get; init; }

    internal void Validate()
    {
        if (RateLimiter is null)
        {
            throw new InvalidOperationException(
                "RateLimiter must be configured. Provide a RateLimiter instance " +
                "(e.g., new ConcurrencyLimiter, new TokenBucketRateLimiter, etc.).");
        }
    }
}

/// <summary>
/// Event arguments for the <see cref="RateLimiterStrategyOptions.OnRejected"/> callback.
/// </summary>
/// <param name="RetryAfter">The suggested wait duration before retrying, or <c>null</c>.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct OnRateLimitRejectedEvent(
    TimeSpan? RetryAfter,
    ResilienceContext Context);
