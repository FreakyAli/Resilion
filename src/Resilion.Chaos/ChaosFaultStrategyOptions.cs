namespace Resilion.Chaos;

/// <summary>
/// Options for the chaos fault strategy, which throws an exception instead of calling inward.
/// </summary>
public sealed record ChaosFaultStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Gets the exception to inject.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Prefer <see cref="FaultGenerator"/>.</strong> <c>Outcome&lt;T&gt;.FromException</c>
    /// captures the exception with <see cref="System.Runtime.ExceptionServices.ExceptionDispatchInfo"/>,
    /// so rethrowing one shared instance repeatedly appends to its stack trace each time and the
    /// traces become progressively less readable.
    /// </para>
    /// <para>Ignored when <see cref="FaultGenerator"/> is set.</para>
    /// </remarks>
    public Exception? Fault { get; init; }

    /// <summary>
    /// Gets a factory producing the exception to inject, overriding <see cref="Fault"/>. Returning
    /// <see langword="null"/> skips injection for that call.
    /// </summary>
    public Func<ChaosGeneratorArgs, Exception?>? FaultGenerator { get; init; }

    /// <summary>Gets a handler invoked after a fault is injected.</summary>
    public ResilienceEventHandler<OnChaosFaultArgs>? OnFaultInjected { get; init; }

    internal void Validate()
    {
        ValidateShared();

        if (Fault is null && FaultGenerator is null)
        {
            throw new InvalidOperationException(
                "A chaos fault strategy needs something to throw: set Fault or FaultGenerator. " +
                "FaultGenerator is preferred — a single reused exception instance accumulates " +
                "stack traces across injections.");
        }
    }
}

/// <summary>Arguments for <see cref="ChaosFaultStrategyOptions.OnFaultInjected"/>.</summary>
/// <param name="Context">The execution context.</param>
/// <param name="Fault">The exception that was injected.</param>
public readonly record struct OnChaosFaultArgs(ResilienceContext Context, Exception Fault);
