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

        var result = OrderingValidator.Validate(strategies);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SingleStrategy_NoWarnings()
    {
        var result = OrderingValidator.Validate([StrategyType.Retry]);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    // ──────────────────────────────────────────────────────────────────
    // CircuitBreaker outside Retry — error (dangerous, essentially always a bug)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CircuitBreakerBeforeRetry_IsAnError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.CircuitBreaker,  // pos 0
            StrategyType.Retry,           // pos 1
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.Single(result.Errors);
        Assert.Contains("CircuitBreaker is outside Retry", result.Errors[0]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CircuitBreakerAfterRetry_NoError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.CircuitBreaker,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(result.AllMessages, w => w.Contains("CircuitBreaker is outside Retry"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Fallback not outermost — error (dangerous, essentially always a bug)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FallbackNotFirst_IsAnError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.Fallback,   // pos 1, not 0
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.Contains(result.Errors, w => w.Contains("Fallback is at position"));
    }

    [Fact]
    public void FallbackFirst_NoError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Fallback,
            StrategyType.Retry,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(result.AllMessages, w => w.Contains("Fallback is at position"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Hedging + Retry together — warning only (sometimes intentional)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HedgingAndRetry_WarnsButIsNotAnError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Hedging,
            StrategyType.Retry,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.Contains(result.Warnings, w => w.Contains("Both Hedging and Retry"));
        Assert.Empty(result.Errors);
    }

    // ──────────────────────────────────────────────────────────────────
    // Too many timeouts — warning only
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreeTimeouts_WarnsButIsNotAnError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Timeout,
            StrategyType.Timeout,
            StrategyType.Timeout,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.Contains(result.Warnings, w => w.Contains("More than 2 Timeout"));
        Assert.Empty(result.Errors);
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

        var result = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(result.AllMessages, w => w.Contains("More than 2 Timeout"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Retry outermost with no timeout — warning only
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RetryOutermostNoTimeout_WarnsButIsNotAnError()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Retry,
            StrategyType.CircuitBreaker,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.Contains(result.Warnings, w => w.Contains("Retry is the outermost strategy with no Timeout"));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RetryWithTimeoutPresent_NoWarning()
    {
        var strategies = new List<StrategyType>
        {
            StrategyType.Timeout,
            StrategyType.Retry,
        };

        var result = OrderingValidator.Validate(strategies);
        Assert.DoesNotContain(result.AllMessages, w => w.Contains("Retry is the outermost"));
    }

    // ──────────────────────────────────────────────────────────────────
    // ThrowOnOrderingErrors (default true) — dangerous misorderings throw at Build()
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ThrowOnOrderingErrors_DefaultsToTrue_ThrowsOnDangerousOrdering()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Pipeline.Create(b =>
            {
                b.AddCircuitBreaker();
                b.AddRetry();
            }));

        Assert.Contains("CircuitBreaker is outside Retry", ex.Message);
    }

    [Fact]
    public void ThrowOnOrderingErrors_False_WarnsInsteadOfThrowing()
    {
        var captured = new List<string>();

        var pipeline = Pipeline.Create(b =>
        {
            b.ThrowOnOrderingErrors = false;
            b.OnValidationWarning = w => captured.Add(w);
            b.AddCircuitBreaker();
            b.AddRetry();
        });

        Assert.NotNull(pipeline);
        Assert.Contains(captured, w => w.Contains("CircuitBreaker is outside Retry"));
    }

    [Fact]
    public void ThrowOnOrderingErrors_DefaultTrue_DoesNotThrowForWarningsOnlyOrdering()
    {
        // Hedging + Retry is a warning, not an error — must not throw even with the default.
        var captured = new List<string>();

        var pipeline = Pipeline.Create<string>(b =>
        {
            b.OnValidationWarning = w => captured.Add(w);
            b.AddHedging(new HedgingStrategyOptions<string> { MaxHedgedAttempts = 1 });
            b.AddRetry();
        });

        Assert.NotNull(pipeline);
        Assert.Contains(captured, w => w.Contains("Both Hedging and Retry"));
    }

    [Fact]
    public void TypedPipeline_ThrowOnOrderingErrors_DefaultsToTrue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Pipeline.Create<string>(b =>
            {
                b.AddCircuitBreaker();
                b.AddRetry();
            }));
    }

    // ──────────────────────────────────────────────────────────────────
    // SuppressOrderingWarnings on builder — suppresses both errors and warnings entirely
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SuppressWarnings_NoWarningsEmitted_AndDoesNotThrow()
    {
        var captured = new List<string>();

        var pipeline = Pipeline.Create(b =>
        {
            b.SuppressOrderingWarnings = true;
            b.OnValidationWarning = w => captured.Add(w);
            b.AddCircuitBreaker();
            b.AddRetry();
        });

        Assert.NotNull(pipeline);
        Assert.Empty(captured);
    }
}
