using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosFaultStrategyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Injection decision — driven by Randomizer, never by chance
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_AlwaysInject_ThrowsConfiguredFault()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));

        Assert.Equal("injected", ex.Message);
    }

    [Fact]
    public async Task Async_NeverInject_PassesThrough()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 1.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public void Sync_AlwaysInject_ThrowsConfiguredFault()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Execute(ct => "real"));
        Assert.Equal("injected", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────
    // Generator precedence
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_FaultGenerator_TakesPrecedenceOverStaticFault()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Fault = new InvalidOperationException("static"),
            FaultGenerator = _ => new TimeoutException("generated"),
        }));

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_FaultGeneratorReturningNull_PassesThrough()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => null,
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_EnabledFalse_NeverInjects()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            Enabled = false,
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_EnabledGenerator_TakesPrecedenceOverEnabled()
    {
        // The documented production guard: Enabled says yes, the generator says no.
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            Enabled = true,
            EnabledGenerator = _ => false,
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_InjectionRateGenerator_TakesPrecedenceOverRate()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 0.0,
            InjectionRateGenerator = _ => 1.0,
            Randomizer = () => 0.5,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    [Fact]
    public async Task Async_InjectionRateGeneratorOutOfRange_IsClamped()
    {
        // Clamped, not thrown: this runs mid-execution, where an exception would be
        // indistinguishable from an injected fault.
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRateGenerator = _ => 42.0,
            Randomizer = () => 0.99,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));

        var never = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRateGenerator = _ => -5.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
        }));

        Assert.Equal("real", await never.ExecuteAsync(ct => new ValueTask<string>("real")));
    }

    // ──────────────────────────────────────────────────────────────────
    // Callback and validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_OnFaultInjected_ReceivesTheInjectedFault()
    {
        Exception? seen = null;
        var pipeline = Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            FaultGenerator = _ => new InvalidOperationException("injected"),
            OnFaultInjected = a => { seen = a.Fault; return default; },
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));

        Assert.NotNull(seen);
        Assert.Equal("injected", seen!.Message);
    }

    [Fact]
    public void Options_NeitherFaultNorGenerator_ThrowsOnAdd()
        => Assert.Throws<InvalidOperationException>(
            () => Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions())));

    [Fact]
    public void Options_InjectionRateOutOfRange_ThrowsOnAdd()
        => Assert.Throws<InvalidOperationException>(
            () => Pipeline.Create(b => b.AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = 1.5,
                Fault = new InvalidOperationException(),
            })));

    [Fact]
    public async Task Async_ConvenienceOverload_InjectsAtGivenRate()
    {
        // Rate 1.0 with the default randomizer: Random.Shared.NextDouble() returns [0,1),
        // which is always < 1.0, so this injects without needing a Randomizer.
        var pipeline = Pipeline.Create(b =>
            b.AddChaosFault(1.0, () => new InvalidOperationException("injected")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
    }
}
