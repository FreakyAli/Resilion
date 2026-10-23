namespace Resilion.Extensions.Configuration;

/// <summary>
/// A bindable description of <see cref="RetryStrategyOptions"/>.
/// </summary>
/// <remarks>
/// Every default below mirrors the options record exactly, so binding nothing is identical to
/// passing no options at all. Drift between the two is a silent behaviour change; the
/// <c>...Defaults_Match...</c> tests exist to pin it.
/// <para>
/// Delegates — <c>ShouldHandle</c>, <c>OnRetry</c> — are absent on purpose: they cannot come from
/// configuration. Layer them on afterwards with a <c>with</c> expression, which needs no extra API:
/// </para>
/// <code>
/// services.Configure&lt;RetryStrategyConfiguration&gt;(config.GetSection("Resilience:Api:Retry"));
/// services.AddResiliencePipeline&lt;RetryStrategyConfiguration&gt;("api", (b, c) =&gt;
///     b.AddRetry(c.ToOptions() with { ShouldHandle = ex =&gt; ex is HttpRequestException }));
/// </code>
/// </remarks>
public sealed class RetryStrategyConfiguration
{
    /// <summary>Gets or sets the retry count. Defaults to 3.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets whether to apply decorrelated jitter. Defaults to <see langword="true"/>.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets a cap applied after the backoff computes its value, for every kind. Defaults to
    /// no cap.
    /// </summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>Gets or sets the backoff shape.</summary>
    public RetryDelayConfiguration Delay { get; set; } = new();

    /// <summary>Projects this description into options, leaving all delegates unset.</summary>
    /// <remarks>
    /// Delegate members stay <see langword="null"/>, so each strategy's own default predicate
    /// applies — the same behaviour as constructing the options by hand and setting nothing.
    /// </remarks>
    public RetryStrategyOptions ToOptions() => new()
    {
        MaxRetryAttempts = MaxRetryAttempts,
        UseJitter = UseJitter,
        MaxDelay = MaxDelay,
        Delay = (Delay ?? new RetryDelayConfiguration()).ToRetryDelay(),
    };

    /// <inheritdoc cref="ToOptions()"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public RetryStrategyOptions<TResult> ToOptions<TResult>() => new()
    {
        MaxRetryAttempts = MaxRetryAttempts,
        UseJitter = UseJitter,
        MaxDelay = MaxDelay,
        Delay = (Delay ?? new RetryDelayConfiguration()).ToRetryDelay(),
    };
}
