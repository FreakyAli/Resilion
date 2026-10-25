using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosOrderingTests
{
    [Fact]
    public void ChaosNotInnermost_DoesNotThrow()
    {
        // The not-innermost rule is a warning, not an error, so building must still succeed.
        // The warning text itself is asserted in Resilion.Tests' OrderingValidatorTests, which
        // can reach the internal validator directly.
        var pipeline = Pipeline.Create(b => b
            .AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = 0.0,
                Fault = new InvalidOperationException(),
            })
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1 }));

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task ChaosInnermost_IsObservedByAnOuterRetry()
    {
        // The end-to-end counterpart to the ordering rule: added last, the fault is inside the
        // retry, so the retry sees it and recovers.
        var injections = 0;
        var pipeline = Pipeline.Create(b => b
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = RetryDelay.Constant(TimeSpan.Zero),
            })
            .AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRateGenerator = _ => injections < 2 ? 1.0 : 0.0,
                Randomizer = () => 0.0,
                FaultGenerator = _ => { injections++; return new InvalidOperationException("injected"); },
            }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
        Assert.Equal(2, injections);
    }
}
