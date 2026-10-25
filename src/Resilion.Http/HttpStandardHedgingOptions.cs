using System.Net.Http;
using Resilion.RateLimiting;

namespace Resilion.Http;

/// <summary>
/// Options for <c>AddStandardHedgingHandler</c>.
/// </summary>
/// <remarks>
/// The resulting pipeline, outermost first, is
/// RateLimiter → <see cref="TotalRequestTimeout"/> → <see cref="Hedging"/> →
/// <see cref="CircuitBreaker"/> → <see cref="AttemptTimeout"/> — the standard shape with hedging
/// in place of retry, for latency-sensitive calls to idempotent endpoints.
/// <para>
/// There is no request-replay setting: hedged attempts can run concurrently and cannot share one
/// <c>HttpRequestMessage</c>, so this handler always clones per attempt, which means it always
/// buffers the request body in memory. Do not use it for large or streaming uploads.
/// </para>
/// <para>
/// All attempts target the same URI. Per-endpoint hedging is not supported.
/// </para>
/// <para>
/// The null-<c>ShouldHandle</c> substitution described on
/// <see cref="HttpStandardResilienceOptions"/> applies here too.
/// </para>
/// </remarks>
public sealed class HttpStandardHedgingOptions
{
    /// <inheritdoc cref="HttpStandardResilienceOptions.TimeProvider"/>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <inheritdoc cref="HttpStandardResilienceOptions.RateLimiter"/>
    public RateLimiterStrategyOptions? RateLimiter { get; set; }

    /// <summary>
    /// Gets or sets the total timeout across all hedged attempts. Defaults to 30 seconds.
    /// </summary>
    public TimeoutStrategyOptions TotalRequestTimeout { get; set; } =
        new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Gets or sets the hedging options. Defaults to 2 attempts with a 2 second delay, so a second
    /// request is issued only if the first has not answered in that time.
    /// </summary>
    public HedgingStrategyOptions<HttpResponseMessage> Hedging { get; set; } = new()
    {
        MaxHedgedAttempts = 2,
        HedgingDelay = TimeSpan.FromSeconds(2),
    };

    /// <inheritdoc cref="HttpStandardResilienceOptions.CircuitBreaker"/>
    public CircuitBreakerStrategyOptions<HttpResponseMessage> CircuitBreaker { get; set; } = new()
    {
        FailureRatioThreshold = 0.1,
        MinimumThroughput = 100,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(5),
    };

    /// <inheritdoc cref="HttpStandardResilienceOptions.AttemptTimeout"/>
    public TimeoutStrategyOptions AttemptTimeout { get; set; } =
        new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Validates the options, throwing if they cannot produce a sensible pipeline.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(TotalRequestTimeout);
        ArgumentNullException.ThrowIfNull(Hedging);
        ArgumentNullException.ThrowIfNull(CircuitBreaker);
        ArgumentNullException.ThrowIfNull(AttemptTimeout);

        if (AttemptTimeout.Timeout != System.Threading.Timeout.InfiniteTimeSpan
            && TotalRequestTimeout.Timeout != System.Threading.Timeout.InfiniteTimeSpan
            && AttemptTimeout.Timeout > TotalRequestTimeout.Timeout)
        {
            throw new InvalidOperationException(
                $"AttemptTimeout ({AttemptTimeout.Timeout}) is longer than TotalRequestTimeout " +
                $"({TotalRequestTimeout.Timeout}), so it can never fire.");
        }
    }
}
