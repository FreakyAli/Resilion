namespace Resilion.Internal;

/// <summary>
/// Identifies the kind of strategy for ordering validation.
/// </summary>
internal enum StrategyType
{
    Unknown,
    Timeout,
    Retry,
    CircuitBreaker,
    Fallback,
    RateLimiter,
    Hedging,
    Custom,

    // Appended, never inserted: the preceding ordinals are load-bearing for tests and benchmarks
    // that compare recorded strategy sequences.
    Chaos,
}
