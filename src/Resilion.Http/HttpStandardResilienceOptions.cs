using System.Net.Http;
using Resilion.RateLimiting;

namespace Resilion.Http;

/// <summary>
/// Options for <c>AddStandardResilienceHandler</c>.
/// </summary>
/// <remarks>
/// The resulting pipeline, outermost first, is
/// RateLimiter → <see cref="TotalRequestTimeout"/> → <see cref="Retry"/> →
/// <see cref="CircuitBreaker"/> → <see cref="AttemptTimeout"/>.
/// <para>
/// Each leg is the corresponding Resilion options record, so anything you can express on a
/// hand-built pipeline you can express here. Adjust one property with a <c>with</c> expression
/// rather than replacing the record, to keep the rest of the defaults:
/// <c>o.Retry = o.Retry with { MaxRetryAttempts = 5 }</c>.
/// </para>
/// <para>
/// <strong>Null <c>ShouldHandle</c> means "use the HTTP default".</strong> If
/// <see cref="Retry"/> or <see cref="CircuitBreaker"/> has a null predicate when the pipeline is
/// built, <see cref="HttpResiliencePredicates.IsTransient"/> is substituted. That way replacing an
/// options record wholesale does not silently lose 5xx handling. Set an explicit predicate to
/// override it — including one that handles exceptions only.
/// </para>
/// </remarks>
public sealed class HttpStandardResilienceOptions
{
    /// <summary>
    /// Gets or sets the time provider used by every strategy in the pipeline.
    /// </summary>
    /// <remarks>
    /// Seeded from the container's <see cref="System.TimeProvider"/> if one is registered, so
    /// injecting a <c>FakeTimeProvider</c> is enough to control timing in tests. Setting this
    /// explicitly wins over the container.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the rate limiter applied before anything else, or <see langword="null"/> to
    /// omit rate limiting.
    /// </summary>
    /// <remarks>
    /// <strong>Null by default</strong>, which is a deliberate deviation from
    /// <c>Microsoft.Extensions.Http.Resilience</c> 8.x — it enabled a 1000-permit concurrency
    /// limiter by default, and 9.x removed it again. A limiter's lifetime belongs to whoever
    /// created it (<c>RateLimiterStrategyOptions.RateLimiter</c> takes an instance and the pipeline
    /// never disposes it), so defaulting it on would mean this package silently owning a limiter
    /// and throttling any application above its permit count. Opt in explicitly:
    /// <code>
    /// var limiter = new ConcurrencyLimiter(new() { PermitLimit = 1000, QueueLimit = 0 });
    /// services.AddSingleton(limiter);                       // the container disposes it
    /// services.AddHttpClient("api").AddStandardResilienceHandler((o, sp) =>
    ///     o.RateLimiter = new RateLimiterStrategyOptions
    ///     {
    ///         RateLimiter = sp.GetRequiredService&lt;ConcurrencyLimiter&gt;(),
    ///     });
    /// </code>
    /// </remarks>
    public RateLimiterStrategyOptions? RateLimiter { get; set; }

    /// <summary>
    /// Gets or sets the total timeout across all retries. Defaults to 30 seconds.
    /// </summary>
    public TimeoutStrategyOptions TotalRequestTimeout { get; set; } =
        new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Gets or sets the retry options. Defaults to 3 attempts with jittered exponential backoff
    /// from 2 seconds.
    /// </summary>
    public RetryStrategyOptions<HttpResponseMessage> Retry { get; set; } = new()
    {
        MaxRetryAttempts = 3,
        Delay = RetryDelay.Exponential(TimeSpan.FromSeconds(2)),
        UseJitter = true,
    };

    /// <summary>
    /// Gets or sets the circuit breaker options. Defaults to a 10% failure ratio over a 30 second
    /// window with a minimum throughput of 100, breaking for 5 seconds.
    /// </summary>
    public CircuitBreakerStrategyOptions<HttpResponseMessage> CircuitBreaker { get; set; } = new()
    {
        FailureRatioThreshold = 0.1,
        MinimumThroughput = 100,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Gets or sets the per-attempt timeout, innermost in the pipeline. Defaults to 10 seconds.
    /// </summary>
    public TimeoutStrategyOptions AttemptTimeout { get; set; } =
        new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Gets or sets whether each attempt sends the caller's request or an independent clone.
    /// Defaults to <see cref="HttpRequestReplay.ReuseRequest"/>.
    /// </summary>
    public HttpRequestReplay RequestReplay { get; set; } = HttpRequestReplay.ReuseRequest;

    /// <summary>Validates the options, throwing if they cannot produce a sensible pipeline.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(TotalRequestTimeout);
        ArgumentNullException.ThrowIfNull(Retry);
        ArgumentNullException.ThrowIfNull(CircuitBreaker);
        ArgumentNullException.ThrowIfNull(AttemptTimeout);

        // A per-attempt timeout longer than the total can never fire, so the caller has almost
        // certainly swapped the two. Failing at build time beats silently ignoring one of them.
        if (AttemptTimeout.Timeout != System.Threading.Timeout.InfiniteTimeSpan
            && TotalRequestTimeout.Timeout != System.Threading.Timeout.InfiniteTimeSpan
            && AttemptTimeout.Timeout > TotalRequestTimeout.Timeout)
        {
            throw new InvalidOperationException(
                $"AttemptTimeout ({AttemptTimeout.Timeout}) is longer than TotalRequestTimeout " +
                $"({TotalRequestTimeout.Timeout}), so it can never fire. The attempt timeout " +
                "bounds one try; the total timeout bounds the whole operation including retries.");
        }
    }
}
