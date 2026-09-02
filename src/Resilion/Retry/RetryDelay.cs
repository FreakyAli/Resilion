namespace Resilion;

/// <summary>
/// Defines how retry delays are computed. Each variant is mutually exclusive by construction,
/// eliminating the confusion of Polly's separate Delay/BackoffType/UseJitter/DelayGenerator properties.
/// </summary>
/// <example>
/// <code>
/// Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(1))          // exponential backoff
/// Delay = RetryDelay.Constant(TimeSpan.FromMilliseconds(200))      // fixed delay
/// Delay = RetryDelay.Linear(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(10))
/// Delay = RetryDelay.Custom(ctx => ComputeMyDelay(ctx))            // fully custom
/// </code>
/// </example>
public abstract record RetryDelay
{
    // Sealed hierarchy — users extend via Custom, not by inheriting from RetryDelay.
    private RetryDelay() { }

    /// <summary>
    /// Creates a constant delay (same duration for every retry).
    /// </summary>
    /// <param name="delay">The fixed delay between retries.</param>
    public static RetryDelay Constant(TimeSpan delay) => new ConstantDelay(delay);

    /// <summary>
    /// Creates a linearly increasing delay (baseDelay × attemptNumber).
    /// </summary>
    /// <param name="baseDelay">The base delay that scales linearly.</param>
    /// <param name="maxDelay">Optional cap on the delay. Defaults to no cap.</param>
    public static RetryDelay Linear(TimeSpan baseDelay, TimeSpan? maxDelay = null)
        => new LinearDelay(baseDelay, maxDelay);

    /// <summary>
    /// Creates an exponentially increasing delay (baseDelay × 2^attemptNumber).
    /// </summary>
    /// <param name="baseDelay">The base delay for the first retry.</param>
    /// <param name="maxDelay">Optional cap on the delay. Defaults to 30 seconds.</param>
    public static RetryDelay Exponential(TimeSpan baseDelay, TimeSpan? maxDelay = null)
        => new ExponentialDelay(baseDelay, maxDelay ?? TimeSpan.FromSeconds(30));

    /// <summary>
    /// Creates a fully custom delay computation.
    /// </summary>
    /// <param name="compute">Function that receives the retry context and returns a delay.</param>
    public static RetryDelay Custom(Func<RetryDelayContext, TimeSpan> compute)
        => new CustomDelay(compute);

    /// <summary>
    /// Zero delay — retries execute immediately with no wait.
    /// </summary>
    public static RetryDelay None { get; } = Constant(TimeSpan.Zero);

    /// <summary>
    /// Computes the delay for the given attempt number.
    /// </summary>
    /// <param name="attemptNumber">The 1-based retry attempt number (1 = first retry, not the original call).</param>
    /// <param name="useJitter">Whether to apply jitter to the computed delay.</param>
    /// <returns>The delay to wait before the next retry.</returns>
    internal abstract TimeSpan ComputeDelay(int attemptNumber, bool useJitter);

    /// <summary>
    /// Applies decorrelated jitter to a delay value.
    /// Uses the AWS recommended algorithm: sleep = min(cap, random_between(base, prev * 3)).
    /// </summary>
    private static TimeSpan ApplyJitter(TimeSpan delay, TimeSpan? maxDelay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var ms = delay.TotalMilliseconds;
        // ±25% jitter for simplicity and effectiveness
        var jitterMs = ms * (0.75 + (Random.Shared.NextDouble() * 0.5));
        var result = TimeSpan.FromMilliseconds(jitterMs);

        if (maxDelay.HasValue && result > maxDelay.Value)
        {
            result = maxDelay.Value;
        }

        return result;
    }

    private static TimeSpan Clamp(TimeSpan delay, TimeSpan? maxDelay)
        => maxDelay.HasValue && delay > maxDelay.Value ? maxDelay.Value : delay;

    // ── Variants ─────────────────────────────────────────────────

    private sealed record ConstantDelay(TimeSpan Delay) : RetryDelay
    {
        internal override TimeSpan ComputeDelay(int attemptNumber, bool useJitter)
            => useJitter ? ApplyJitter(Delay, null) : Delay;
    }

    private sealed record LinearDelay(TimeSpan BaseDelay, TimeSpan? MaxDelay) : RetryDelay
    {
        internal override TimeSpan ComputeDelay(int attemptNumber, bool useJitter)
        {
            // Prevent overflow: clamp total milliseconds before constructing TimeSpan
            var baseMs = BaseDelay.TotalMilliseconds;
            var maxMs = MaxDelay?.TotalMilliseconds ?? double.MaxValue;
            var delayMs = Math.Min(baseMs * attemptNumber, maxMs);

            // Guard against overflow when jitter multiplier (up to 1.25x) is applied.
            // Match the pattern in ExponentialDelay to handle edge cases consistently.
            if (double.IsInfinity(delayMs) || double.IsNaN(delayMs) || delayMs > TimeSpan.MaxValue.TotalMilliseconds)
            {
                delayMs = TimeSpan.MaxValue.TotalMilliseconds;
            }

            var delay = TimeSpan.FromMilliseconds(delayMs);
            return useJitter ? ApplyJitter(delay, MaxDelay) : delay;
        }
    }

    private sealed record ExponentialDelay(TimeSpan BaseDelay, TimeSpan? MaxDelay) : RetryDelay
    {
        internal override TimeSpan ComputeDelay(int attemptNumber, bool useJitter)
        {
            // delay = baseDelay * 2^(attemptNumber-1)
            var multiplier = Math.Pow(2, attemptNumber - 1);
            var delayMs = BaseDelay.TotalMilliseconds * multiplier;

            // Clamp before TimeSpan.FromMilliseconds to avoid OverflowException
            // when attemptNumber is large (e.g., int.MaxValue retries).
            if (MaxDelay.HasValue)
            {
                delayMs = Math.Min(delayMs, MaxDelay.Value.TotalMilliseconds);
            }
            else if (double.IsInfinity(delayMs) || double.IsNaN(delayMs) || delayMs > TimeSpan.MaxValue.TotalMilliseconds)
            {
                delayMs = TimeSpan.MaxValue.TotalMilliseconds;
            }

            var delay = TimeSpan.FromMilliseconds(delayMs);
            return useJitter ? ApplyJitter(delay, MaxDelay) : delay;
        }
    }

    private sealed record CustomDelay(Func<RetryDelayContext, TimeSpan> Compute) : RetryDelay
    {
        internal override TimeSpan ComputeDelay(int attemptNumber, bool useJitter)
            => Compute(new RetryDelayContext(attemptNumber));
    }
}

/// <summary>
/// Context passed to <see cref="RetryDelay.Custom"/> delegates.
/// </summary>
/// <param name="AttemptNumber">The 1-based retry attempt number.</param>
public readonly record struct RetryDelayContext(int AttemptNumber);
