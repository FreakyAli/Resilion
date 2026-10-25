using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// <see cref="CircuitBreakerStateProvider"/> — the only public way to observe a circuit's state.
/// </summary>
public class CircuitBreakerStateProviderTests
{
    [Fact]
    public async Task Async_StateProvider_ReflectsClosedThenOpenThenHalfOpen()
    {
        var fakeTime = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var pipeline = Pipeline.Create(b =>
        {
            b.TimeProvider = fakeTime;
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(10),
                StateProvider = state,
            });
        });

        Assert.Equal(CircuitState.Closed, state.State);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pipeline.ExecuteAsync<string>(
                    ct => throw new InvalidOperationException("boom")));
        }

        Assert.Equal(CircuitState.Open, state.State);

        // Past the break duration the circuit admits a trial call, which is the HalfOpen probe.
        fakeTime.Advance(TimeSpan.FromSeconds(11));
        await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));

        Assert.Equal(CircuitState.Closed, state.State);
    }

    [Fact]
    public void StateProvider_Unbound_ThrowsOnStateRead()
    {
        var state = new CircuitBreakerStateProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => state.State);

        Assert.Contains("not associated with", ex.Message);
    }

    [Fact]
    public void StateProvider_BoundTwice_Throws()
    {
        var state = new CircuitBreakerStateProvider();
        var options = new CircuitBreakerStrategyOptions { StateProvider = state };

        _ = Pipeline.Create(b => b.AddCircuitBreaker(options));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Pipeline.Create(b => b.AddCircuitBreaker(options)));

        Assert.Contains("already bound", ex.Message);
    }

    [Fact]
    public async Task Async_StateProvider_WorksOnTypedCircuitBreaker()
    {
        var state = new CircuitBreakerStateProvider();
        var pipeline = Pipeline.Create<string>(b => b.AddCircuitBreaker(
            new CircuitBreakerStrategyOptions<string>
            {
                FailureRatioThreshold = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(10),
                ShouldHandle = o => o.TryGetResult(out var r) && r == "fail",
                StateProvider = state,
            }));

        Assert.Equal(CircuitState.Closed, state.State);

        for (var i = 0; i < 2; i++)
        {
            await pipeline.ExecuteAsync(ct => new ValueTask<string>("fail"));
        }

        Assert.Equal(CircuitState.Open, state.State);
    }

    [Fact]
    public async Task Async_StateProvider_ReportsIsolatedAfterManualIsolate()
    {
        var state = new CircuitBreakerStateProvider();
        var control = new CircuitBreakerManualControl();
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ManualControl = control,
            StateProvider = state,
        }));

        await control.IsolateAsync();

        Assert.Equal(CircuitState.Isolated, state.State);

        await control.ResetAsync();

        Assert.Equal(CircuitState.Closed, state.State);
        await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
    }
}
