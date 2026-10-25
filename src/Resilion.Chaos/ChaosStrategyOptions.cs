namespace Resilion.Chaos;

/// <summary>
/// Arguments passed to every chaos generator.
/// </summary>
/// <param name="Context">The execution context for the call being considered for injection.</param>
public readonly record struct ChaosGeneratorArgs(ResilienceContext Context);

/// <summary>
/// Settings shared by every chaos strategy: whether it is active, how often it injects, and
/// where the injection decision's randomness comes from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read this before using chaos outside a test.</strong> The defaults are
/// <see cref="Enabled"/> <c>= true</c> and <see cref="InjectionRate"/> <c>= 0.001</c>, so a
/// strategy added to a pipeline starts injecting immediately. Gate it on the environment:
/// </para>
/// <code>
/// EnabledGenerator = _ =&gt; !env.IsProduction()
/// </code>
/// <para>
/// Every generator takes precedence over its corresponding static value, uniformly across all
/// four strategies.
/// </para>
/// </remarks>
public abstract record ChaosStrategyOptions
{
    /// <summary>
    /// Gets the probability of injecting, from 0.0 (never) to 1.0 (always). Defaults to
    /// <c>0.001</c> — one call in a thousand.
    /// </summary>
    /// <remarks>Ignored when <see cref="InjectionRateGenerator"/> is set.</remarks>
    public double InjectionRate { get; init; } = 0.001;

    /// <summary>
    /// Gets a per-call injection probability, overriding <see cref="InjectionRate"/>.
    /// </summary>
    /// <remarks>
    /// A value outside <c>[0, 1]</c> is clamped rather than thrown, because this runs mid-execution
    /// where an exception would be indistinguishable from an injected fault.
    /// </remarks>
    public Func<ChaosGeneratorArgs, double>? InjectionRateGenerator { get; init; }

    /// <summary>Gets whether this strategy is active at all. Defaults to <see langword="true"/>.</summary>
    /// <remarks>Ignored when <see cref="EnabledGenerator"/> is set.</remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets a per-call enablement check, overriding <see cref="Enabled"/>. This is the intended
    /// place for an environment guard.
    /// </summary>
    public Func<ChaosGeneratorArgs, bool>? EnabledGenerator { get; init; }

    /// <summary>
    /// Gets the source of randomness for the injection decision, returning a value in
    /// <c>[0, 1)</c>. Defaults to <see cref="Random.Shared"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the supported test seam, and it is not optional polish.</strong> Without
    /// it a chaos strategy is as untestable as a strategy that ignores
    /// <c>PipelineBuilder.TimeProvider</c> — you could only assert on injection statistically.
    /// </para>
    /// <code>
    /// Randomizer = () =&gt; 0.0   // always inject
    /// Randomizer = () =&gt; 1.0   // never inject
    /// </code>
    /// <para>
    /// It is consulted on every decision with no short-circuit, so those two values hold
    /// regardless of the configured rate.
    /// </para>
    /// </remarks>
    public Func<double>? Randomizer { get; init; }

    private protected void ValidateShared()
    {
        if (InjectionRateGenerator is null && (InjectionRate < 0.0 || InjectionRate > 1.0))
        {
            throw new InvalidOperationException(
                $"InjectionRate must be between 0.0 and 1.0 inclusive, but was {InjectionRate}. " +
                "Use InjectionRateGenerator for a value computed per call.");
        }
    }
}

/// <summary>
/// The single place the inject-or-not decision is made, so the four strategies cannot drift
/// apart on generator precedence, clamping, or which randomizer is used.
/// </summary>
internal static class ChaosDecision
{
    internal static bool ShouldInject(ChaosStrategyOptions options, ResilienceContext context)
    {
        var args = new ChaosGeneratorArgs(context);

        if (!(options.EnabledGenerator?.Invoke(args) ?? options.Enabled))
        {
            return false;
        }

        var rate = Math.Clamp(options.InjectionRateGenerator?.Invoke(args) ?? options.InjectionRate, 0.0, 1.0);
        if (rate <= 0.0)
        {
            return false;
        }

        // No short-circuit at rate >= 1.0: the randomizer is consulted on every decision so that
        // Randomizer = () => 1.0 reliably means "never inject" at any configured rate.
        return (options.Randomizer ?? Random.Shared.NextDouble)() < rate;
    }

    internal static void RecordInjection(string strategyName, ResilienceContext context)
        => ResilionTelemetry.ChaosInjections.Add(
            1,
            new(ResilionTelemetry.PipelineNameTag, context.PipelineName),
            new(ResilionTelemetry.OperationKeyTag, context.OperationKey),
            new(ResilionTelemetry.ChaosStrategyTag, strategyName));
}
