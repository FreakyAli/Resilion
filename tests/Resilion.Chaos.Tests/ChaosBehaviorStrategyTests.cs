using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosBehaviorStrategyTests
{
    [Fact]
    public async Task Async_AlwaysInject_InvokesBehavior()
    {
        var ran = 0;
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = _ => { ran++; return default; },
        }));

        Assert.Equal("real", await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Sync_AlwaysInject_InvokesBehavior()
    {
        var ran = 0;
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = _ => { ran++; return default; },
        }));

        Assert.Equal("real", pipeline.Execute(ct => "real"));
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Async_NeverInject_DoesNotInvokeBehavior()
    {
        var ran = 0;
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 1.0,
            Behavior = _ => { ran++; return default; },
        }));

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task Async_AsyncBehavior_IsAwaitedBeforeTheCallback()
    {
        var order = new List<string>();
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = async _ =>
            {
                await Task.Yield();
                order.Add("behavior");
            },
        }));

        await pipeline.ExecuteAsync(ct => { order.Add("callback"); return new ValueTask<string>("real"); });

        Assert.Equal(new[] { "behavior", "callback" }, order);
    }

    [Fact]
    public void Sync_AsyncBehavior_IsAwaitedNotFireAndForget()
    {
        // Guards against calling the delegate's own Invoke, which returns a ValueTask that is
        // then discarded: the payload would start and the pipeline would carry on without it.
        var completed = false;
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = async _ =>
            {
                await Task.Delay(50);
                completed = true;
            },
        }));

        Assert.Equal("real", pipeline.Execute(ct => "real"));
        Assert.True(completed, "The async behavior was not awaited before the callback ran.");
    }

    [Fact]
    public async Task Async_BehaviorThrows_ExceptionPropagates()
    {
        // Deliberately not caught by the strategy: a behavior that throws is a fault the
        // pipeline is supposed to see.
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = _ => throw new InvalidOperationException("boom"),
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Async_Behavior_ReceivesTheExecutionContext()
    {
        string? seenKey = null;
        var pipeline = Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Behavior = a => { seenKey = a.Context.OperationKey; return default; },
        }));

        // ExecuteOutcomeAsync is the only untyped entry point that accepts a caller-owned
        // context, which is how OperationKey gets set.
        var context = ResilienceContextPool.Shared.Rent();
        context.OperationKey = "op-key";
        try
        {
            await pipeline.ExecuteOutcomeAsync(
                static (_, _) => new ValueTask<Outcome<string>>(Outcome<string>.FromResult("real")),
                0,
                context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        Assert.Equal("op-key", seenKey);
    }

    [Fact]
    public void Options_NoBehaviorHandler_ThrowsOnAdd()
        => Assert.Throws<InvalidOperationException>(
            () => Pipeline.Create(b => b.AddChaosBehavior(new ChaosBehaviorStrategyOptions())));
}
