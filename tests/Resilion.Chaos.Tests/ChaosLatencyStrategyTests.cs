using Microsoft.Extensions.Time.Testing;
using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosLatencyStrategyTests
{
    [Fact]
    public async Task Async_AlwaysInject_DelaysByConfiguredLatency()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                Latency = TimeSpan.FromSeconds(5),
            });
        });

        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real")).AsTask();

        // Not yet: the delay is driven by the fake clock, so nothing has elapsed.
        Assert.False(task.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("real", await task);
    }

    [Fact]
    public async Task Async_UsesBuilderTimeProvider_NotSystem()
    {
        // If the strategy read TimeProvider.System this would hang for 10 real minutes rather
        // than completing the moment the fake clock advances.
        var time = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(1.0, TimeSpan.FromMinutes(10));
        });

        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real")).AsTask();
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal("real", await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Async_NeverInject_DoesNotDelay()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 1.0,
                Latency = TimeSpan.FromHours(1),
            });
        });

        // Completes without the clock moving at all. WaitAsync so that a regression here fails
        // fast instead of hanging CI forever on a delay the fake clock will never advance.
        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real")).AsTask();
        Assert.Equal("real", await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Async_LatencyGenerator_TakesPrecedence()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                Latency = TimeSpan.FromHours(1),
                LatencyGenerator = _ => TimeSpan.FromSeconds(2),
            });
        });

        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real")).AsTask();
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal("real", await task);
    }

    [Fact]
    public async Task Async_NegativeLatency_ClampedToZero()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                LatencyGenerator = _ => TimeSpan.FromSeconds(-30),
            });
        });

        // Clamped, so it completes without advancing the clock. WaitAsync for the same
        // fail-fast reason as above.
        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real")).AsTask();
        Assert.Equal("real", await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Async_Cancellation_DuringInjectedLatency_Propagates()
    {
        var time = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = time;
            b.AddChaosLatency(1.0, TimeSpan.FromMinutes(5));
        });

        var task = pipeline.ExecuteAsync(ct => new ValueTask<string>("real"), cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void Sync_AlwaysInject_DelaysByConfiguredLatency()
    {
        // The sync path waits on CancellationToken.WaitHandle, which has no TimeProvider
        // equivalent, so this is real wall-clock time — kept tiny deliberately. Documented on
        // the strategy; same limitation as synchronous retry delays (future-plans #9).
        var pipeline = Pipeline.Create(b => b.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 0.0,
            Latency = TimeSpan.FromMilliseconds(120),
        }));

        var started = DateTimeOffset.UtcNow;
        Assert.Equal("real", pipeline.Execute(ct => "real"));

        Assert.True(
            DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(80),
            "Sync latency injection should have blocked for roughly the configured delay.");
    }

    [Fact]
    public void Sync_NeverInject_DoesNotDelay()
    {
        var pipeline = Pipeline.Create(b => b.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = 1.0,
            Randomizer = () => 1.0,
            Latency = TimeSpan.FromSeconds(30),
        }));

        var started = DateTimeOffset.UtcNow;
        Assert.Equal("real", pipeline.Execute(ct => "real"));
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Options_NegativeLatency_ThrowsOnAdd()
        => Assert.Throws<InvalidOperationException>(
            () => Pipeline.Create(b => b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                Latency = TimeSpan.FromSeconds(-1),
            })));
}
