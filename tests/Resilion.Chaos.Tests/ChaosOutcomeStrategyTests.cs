using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosOutcomeStrategyTests
{
    [Fact]
    public async Task Async_AlwaysInject_ReturnsConfiguredOutcome()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Outcome = Outcome<string>.FromResult("chaos"),
        }));

        Assert.Equal("chaos", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public void Sync_AlwaysInject_ReturnsConfiguredOutcome()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Outcome = Outcome<string>.FromResult("chaos"),
        }));

        Assert.Equal("chaos", pipeline.Execute(ct => "real"));
    }

    [Fact]
    public async Task Async_NeverInject_PassesThrough()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1.0,
            Randomizer = () => 1.0,
            Outcome = Outcome<string>.FromResult("chaos"),
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_OutcomeGenerator_TakesPrecedence()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Outcome = Outcome<string>.FromResult("static"),
            OutcomeGenerator = _ => Outcome<string>.FromResult("generated"),
        }));

        Assert.Equal("generated", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    // ──────────────────────────────────────────────────────────────────
    // The reason chaos must be innermost
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_InjectedFailureOutcome_IsSeenByOuterRetry()
    {
        // Chaos is added last, so it sits inside Retry and Retry observes the injected failure.
        // This is the whole point of the innermost rule: with the order reversed the retry would
        // never see the fault and the chaos run would silently prove nothing.
        var injections = 0;
        var pipeline = Pipeline.Create<string>(b => b
            .AddRetry(new RetryStrategyOptions<string>
            {
                MaxRetryAttempts = 2,
                Delay = RetryDelay.Constant(TimeSpan.Zero),
                ShouldHandle = o => o.IsFailure,
            })
            .AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
            {
                // Inject on the first two attempts, then let the real call through.
                InjectionRateGenerator = _ => injections < 2 ? 1.0 : 0.0,
                Randomizer = () => 0.0,
                OutcomeGenerator = _ =>
                {
                    injections++;
                    return Outcome<string>.FromException(new InvalidOperationException("injected"));
                },
            }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
        Assert.Equal(2, injections);
    }

    [Fact]
    public async Task Async_InjectedResultValue_IsSeenByResultBasedRetry()
    {
        // A result-only predicate: a chaos *fault* could never trigger this, which is exactly
        // why AddChaosOutcome exists alongside AddChaosFault.
        var attempts = 0;
        var injected = false;
        var pipeline = Pipeline.Create<int>(b => b
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.Constant(TimeSpan.Zero),
                ShouldHandle = o => o.TryGetResult(out var r) && r == 503,
            })
            .AddChaosOutcome(new ChaosOutcomeStrategyOptions<int>
            {
                InjectionRateGenerator = _ => injected ? 0.0 : 1.0,
                Randomizer = () => 0.0,
                OutcomeGenerator = _ => { injected = true; return Outcome<int>.FromResult(503); },
            }));

        var result = await pipeline.ExecuteAsync(ct => { attempts++; return new ValueTask<int>(200); });

        Assert.Equal(200, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Options_NeitherOutcomeNorGenerator_ThrowsOnAdd()
        => Assert.Throws<InvalidOperationException>(
            () => Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>())));

    [Fact]
    public async Task Async_ConvenienceOverload_InjectsResult()
    {
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(1.0, () => "chaos"));
        Assert.Equal("chaos", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_OnOutcomeInjected_ReceivesTheInjectedOutcome()
    {
        string? seen = null;
        var pipeline = Pipeline.Create<string>(b => b.AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Outcome = Outcome<string>.FromResult("chaos"),
            OnOutcomeInjected = a => { seen = a.Outcome.GetResultOrDefault(); return default; },
        }));

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));
        Assert.Equal("chaos", seen);
    }
}
