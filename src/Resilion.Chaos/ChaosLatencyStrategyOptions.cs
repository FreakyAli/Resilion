namespace Resilion.Chaos;

/// <summary>
/// Options for the chaos latency strategy, which delays before calling inward.
/// </summary>
/// <remarks>
/// Latency injection is how you test a timeout. Place it innermost, inside the attempt timeout
/// you want to trip.
/// </remarks>
public sealed record ChaosLatencyStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Gets the delay to inject. Defaults to 30 seconds.</summary>
    /// <remarks>Ignored when <see cref="LatencyGenerator"/> is set.</remarks>
    public TimeSpan Latency { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets a per-call delay, overriding <see cref="Latency"/>. A negative result is clamped to
    /// zero rather than thrown, since this runs mid-execution.
    /// </summary>
    public Func<ChaosGeneratorArgs, TimeSpan>? LatencyGenerator { get; init; }

    /// <summary>Gets a handler invoked after a delay is injected.</summary>
    public ResilienceEventHandler<OnChaosLatencyArgs>? OnLatencyInjected { get; init; }

    internal void Validate()
    {
        ValidateShared();

        if (LatencyGenerator is null && Latency < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Latency must not be negative, but was {Latency}. " +
                "Use LatencyGenerator for a value computed per call.");
        }
    }
}

/// <summary>Arguments for <see cref="ChaosLatencyStrategyOptions.OnLatencyInjected"/>.</summary>
/// <param name="Context">The execution context.</param>
/// <param name="Latency">The delay that was injected.</param>
public readonly record struct OnChaosLatencyArgs(ResilienceContext Context, TimeSpan Latency);
