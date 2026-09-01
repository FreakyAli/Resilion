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

    internal static readonly Counter<long> StrategyExecutions = Meter.CreateCounter<long>(
        "resilion.strategy.executions",
        description: "Total strategy executions.");

    internal static readonly Histogram<double> StrategyDuration = Meter.CreateHistogram<double>(
        "resilion.strategy.duration",
        unit: "s",
        description: "Strategy execution duration in seconds.");

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
