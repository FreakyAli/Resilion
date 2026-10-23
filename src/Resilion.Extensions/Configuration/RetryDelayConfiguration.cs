namespace Resilion.Extensions.Configuration;

/// <summary>Selects which <see cref="RetryDelay"/> variant to build.</summary>
/// <remarks>
/// There is deliberately no <c>Custom</c> member. <see cref="RetryDelay.Custom"/> takes a delegate,
/// which cannot come from a configuration file; offering it here would mean a value that silently
/// does nothing when bound.
/// </remarks>
public enum RetryDelayKind
{
    /// <summary>The same delay between every attempt.</summary>
    Constant = 0,

    /// <summary>Delay grows linearly with the attempt number.</summary>
    Linear = 1,

    /// <summary>Delay doubles with each attempt.</summary>
    Exponential = 2,
}

/// <summary>
/// A bindable description of a <see cref="RetryDelay"/>.
/// </summary>
/// <remarks>
/// <see cref="RetryDelay"/> is a closed union with a private constructor — exactly one backoff
/// strategy can be configured, and the compiler enforces it. That is deliberate, and making it
/// directly bindable would reintroduce the ambiguity it exists to prevent. This type is the
/// one-way funnel instead: a mutable POCO the configuration binder can populate, projected into
/// the real union by <see cref="ToRetryDelay"/>.
/// <para>
/// A cap on the computed delay is <em>not</em> here. <c>RetryStrategyConfiguration.MaxDelay</c>
/// carries it, because the strategy-level cap applies to every kind — including
/// <see cref="RetryDelayKind.Constant"/>, whose factory takes no maximum at all. Exposing a cap in
/// both places would be two knobs for one thing.
/// </para>
/// </remarks>
public sealed class RetryDelayConfiguration
{
    /// <summary>Gets or sets the backoff shape. Defaults to <see cref="RetryDelayKind.Exponential"/>.</summary>
    public RetryDelayKind Kind { get; set; } = RetryDelayKind.Exponential;

    /// <summary>
    /// Gets or sets the delay for <see cref="RetryDelayKind.Constant"/>, or the first delay that
    /// later attempts grow from. Defaults to 1 second, matching <c>RetryStrategyOptions.Delay</c>.
    /// </summary>
    /// <remarks>Binds from a <c>TimeSpan</c> string such as <c>"00:00:01"</c>.</remarks>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Projects this description into a <see cref="RetryDelay"/>.</summary>
    public RetryDelay ToRetryDelay() => Kind switch
    {
        RetryDelayKind.Constant => RetryDelay.Constant(BaseDelay),
        RetryDelayKind.Linear => RetryDelay.Linear(BaseDelay),
        RetryDelayKind.Exponential => RetryDelay.Exponential(BaseDelay),
        _ => throw new InvalidOperationException(
            $"Unknown RetryDelayKind '{Kind}'. Valid values are Constant, Linear and Exponential; " +
            "RetryDelay.Custom takes a delegate and cannot be configured from a file."),
    };
}
