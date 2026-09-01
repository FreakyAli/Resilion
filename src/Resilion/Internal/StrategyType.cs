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
}
