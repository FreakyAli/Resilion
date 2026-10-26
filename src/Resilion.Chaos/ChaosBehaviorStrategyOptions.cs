namespace Resilion.Chaos;

/// <summary>
/// Options for the chaos behavior strategy, which runs an arbitrary payload before calling
/// inward — killing a dependency, filling a disk, dropping a connection.
/// </summary>
/// <remarks>
/// <see cref="Behavior"/> reuses <see cref="ResilienceEventHandler{TArgs}"/> because it already
/// expresses "synchronous or asynchronous, caller's choice" and already handles running a
/// synchronous payload safely under a <see cref="SynchronizationContext"/>. The type's name says
/// "event handler", but what it models is a sync-or-async callback, which is exactly what is
/// needed here. A synchronous payload returns <c>default</c>.
/// </remarks>
public sealed record ChaosBehaviorStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Gets the payload to run on injection. Required.</summary>
    public ResilienceEventHandler<ChaosGeneratorArgs>? Behavior { get; init; }

    /// <summary>Gets a handler invoked after the behavior runs.</summary>
    public ResilienceEventHandler<OnChaosBehaviorArgs>? OnBehaviorInjected { get; init; }

    internal void Validate()
    {
        ValidateShared();

        if (Behavior is null)
        {
            throw new InvalidOperationException(
                "A chaos behavior strategy needs something to run: set Behavior to a handler such " +
                "as 'args => { proxy.DropConnections(); return default; }'.");
        }
    }
}

/// <summary>Arguments for <see cref="ChaosBehaviorStrategyOptions.OnBehaviorInjected"/>.</summary>
/// <param name="Context">The execution context.</param>
public readonly record struct OnChaosBehaviorArgs(ResilienceContext Context);
