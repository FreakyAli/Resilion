using Resilion;

namespace Resilion.RateLimiting;

/// <summary>
/// Thrown when the rate limiter rejects an execution because the rate limit has been exceeded.
/// </summary>
public sealed class RateLimitRejectedException : ResilionException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitRejectedException"/> class.
    /// </summary>
    /// <param name="retryAfter">The suggested duration to wait before retrying, or <c>null</c> if not available.</param>
    internal RateLimitRejectedException(TimeSpan? retryAfter)
        : base(retryAfter.HasValue
            ? $"The rate limit has been exceeded. Retry after {retryAfter.Value.TotalSeconds:F1}s."
            : "The rate limit has been exceeded.")
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the suggested duration to wait before retrying, extracted from the rate limiter's
    /// lease metadata. Returns <c>null</c> if the rate limiter did not provide this information.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
