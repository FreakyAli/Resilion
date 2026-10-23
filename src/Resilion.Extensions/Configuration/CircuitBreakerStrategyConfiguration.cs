namespace Resilion.Extensions.Configuration;

/// <summary>A bindable description of <see cref="CircuitBreakerStrategyOptions"/>.</summary>
/// <remarks>
/// Defaults mirror the options record exactly. <c>ManualControl</c>, <c>StateProvider</c>,
/// <c>BreakDurationGenerator</c> and the event handlers are absent: they are object references or
/// delegates, not configuration values.
/// </remarks>
public sealed class CircuitBreakerStrategyConfiguration
{
    /// <summary>Gets or sets the failure ratio that trips the circuit. Defaults to 0.5.</summary>
    public double FailureRatioThreshold { get; set; } = 0.5;

    /// <summary>Gets or sets the rolling window the ratio is measured over. Defaults to 30 seconds.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the calls required before the ratio is evaluated. Defaults to 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>Gets or sets how long the circuit stays open. Defaults to 30 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Projects this description into options, leaving all delegates unset.</summary>
    public CircuitBreakerStrategyOptions ToOptions() => new()
    {
        FailureRatioThreshold = FailureRatioThreshold,
        SamplingDuration = SamplingDuration,
        MinimumThroughput = MinimumThroughput,
        BreakDuration = BreakDuration,
    };

    /// <inheritdoc cref="ToOptions()"/>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    public CircuitBreakerStrategyOptions<TResult> ToOptions<TResult>() => new()
    {
        FailureRatioThreshold = FailureRatioThreshold,
        SamplingDuration = SamplingDuration,
        MinimumThroughput = MinimumThroughput,
        BreakDuration = BreakDuration,
    };
}
