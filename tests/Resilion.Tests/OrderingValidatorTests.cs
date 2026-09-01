using Resilion.Internal;
using Xunit;

namespace Resilion.Tests;

public class OrderingValidatorTests
{
    // ──────────────────────────────────────────────────────────────────
    // No warnings for correct ordering
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CanonicalOrder_NoWarnings()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.RateLimiter,
            StrategyType.Timeout,       // total
            StrategyType.Retry,
            StrategyType.CircuitBreaker,
            StrategyType.Timeout,       // per-attempt
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SingleStrategy_NoWarnings()
    {
        var warnings = OrderingValidator.Validate([StrategyType.Retry]);
        Assert.Empty(warnings);
    }

    // ──────────────────────────────────────────────────────────────────
    // CircuitBreaker outside Retry
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CircuitBreakerBeforeRetry_Warns()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.CircuitBreaker,  // pos 0
            StrategyType.Retry,           // pos 1
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Single(warnings);
        Assert.Contains("CircuitBreaker is outside Retry", warnings[0]);
    }

    [Fact]
    public void CircuitBreakerAfterRetry_NoWarning()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.CircuitBreaker,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(warnings, w => w.Contains("CircuitBreaker is outside Retry"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Fallback not outermost
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FallbackNotFirst_Warns()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.Fallback,   // pos 1, not 0
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Contains(warnings, w => w.Contains("Fallback is at position"));
    }

    [Fact]
    public void FallbackFirst_NoWarning()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Fallback,
            StrategyType.Retry,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(warnings, w => w.Contains("Fallback is at position"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Hedging + Retry together
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HedgingAndRetry_Warns()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Hedging,
            StrategyType.Retry,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Contains(warnings, w => w.Contains("Both Hedging and Retry"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Too many timeouts
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreeTimeouts_Warns()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Timeout,
            StrategyType.Timeout,
            StrategyType.Timeout,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Contains(warnings, w => w.Contains("More than 2 Timeout"));
    }

    [Fact]
    public void TwoTimeouts_NoWarning()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Timeout,
            StrategyType.Retry,
            StrategyType.Timeout,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(warnings, w => w.Contains("More than 2 Timeout"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Retry outermost with no timeout
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RetryOutermostNoTimeout_Warns()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.CircuitBreaker,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.Contains(warnings, w => w.Contains("Retry is the outermost strategy with no Timeout"));
    }

    [Fact]
    public void RetryWithTimeoutPresent_NoWarning()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Timeout,
            StrategyType.Retry,
        };

        var warnings = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(warnings, w => w.Contains("Retry is the outermost"));
    }

    // ──────────────────────────────────────────────────────────────────
    // SuppressOrderingWarnings on builder
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SuppressWarnings_NoWarningsEmitted()
    {
        var captured = new List<string>();

        var pipeline = Pipeline.Create(b =>
        {
            b.SuppressOrderingWarnings = true;
            b.OnValidationWarning = w => captured.Add(w);
            b.AddCircuitBreaker();
            b.AddRetry();
        });

        Assert.Empty(captured);
    }

    // ──────────────────────────────────────────────────────────────────
    // OnValidationWarning callback receives warnings
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void OnValidationWarning_ReceivesWarnings()
    {
        var captured = new List<string>();

        var pipeline = Pipeline.Create(b =>
        {
            b.OnValidationWarning = w => captured.Add(w);
            b.AddCircuitBreaker();
            b.AddRetry();
        });

        Assert.NotEmpty(captured);
        Assert.Contains(captured, w => w.Contains("CircuitBreaker is outside Retry"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline also validates
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TypedPipeline_AlsoValidates()
    {
        var captured = new List<string>();

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.OnValidationWarning = w => captured.Add(w);
            b.AddCircuitBreaker();
            b.AddRetry();
        });

        Assert.NotEmpty(captured);
        Assert.Contains(captured, w => w.Contains("CircuitBreaker is outside Retry"));
    }
}
