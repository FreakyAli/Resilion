using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Resilion;

/// <summary>
/// Provides built-in telemetry instruments for Resilion strategies.
/// Zero overhead when no listener is attached — instruments only allocate when subscribed.
/// </summary>
/// <remarks>
/// Subscribe with <c>.AddMeter("Resilion")</c> for metrics and <c>.AddSource("Resilion")</c> for traces.
/// </remarks>
public static class ResilionTelemetry
{
    /// <summary>Meter name for all Resilion metrics.</summary>
    public const string MeterName = "Resilion";

    /// <summary>ActivitySource name for all Resilion traces.</summary>
    public const string ActivitySourceName = "Resilion";

    internal static readonly Meter Meter = new(MeterName);
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Tag key for the pipeline name in telemetry events.
    /// </summary>
    public const string PipelineNameTag = "pipeline.name";

    /// <summary>
    /// Tag key for the operation key in telemetry events.
    /// </summary>
    public const string OperationKeyTag = "operation.key";

    // Pipeline-level execution count and duration instruments are planned for a future
    // telemetry-wrapping component. They are intentionally omitted here rather than declared
    // unused — the per-strategy counters below are the only instruments this meter emits today.

    internal static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "resilion.retry.attempts",
        description: "Total retry attempts.");

    internal static readonly Counter<long> CircuitBreakerStateChanges = Meter.CreateCounter<long>(
        "resilion.circuit_breaker.state_changes",
        description: "Circuit breaker state transitions.");

    internal static readonly Counter<long> TimeoutExpirations = Meter.CreateCounter<long>(
        "resilion.timeout.expirations",
        description: "Timeout expirations.");

    internal static readonly Counter<long> FallbackActivations = Meter.CreateCounter<long>(
        "resilion.fallback.activations",
        description: "Fallback activations.");

    internal static readonly Counter<long> HedgingAttempts = Meter.CreateCounter<long>(
        "resilion.hedging.attempts",
        description: "Hedging attempts launched.");

    internal static readonly Counter<long> RateLimiterRejections = Meter.CreateCounter<long>(
        "resilion.rate_limiter.rejections",
        description: "Rate limiter rejections.");
}
