using System.Diagnostics.Metrics;
using Resilion.Chaos;
using Xunit;

namespace Resilion.Chaos.Tests;

public class ChaosTelemetryTests
{
    /// <summary>
    /// Records <c>resilion.chaos.injections</c> for one pipeline name.
    /// </summary>
    /// <remarks>
    /// The filter on pipeline name is load-bearing, not tidiness: <see cref="MeterListener"/> is
    /// process-global and xunit runs test classes in parallel, so without it a listener sees
    /// injections from whatever else is running and the assertions become flaky.
    /// </remarks>
    private sealed class InjectionTracker : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly string _pipelineName;
        private readonly object _gate = new();

        internal List<(string Strategy, string? OperationKey)> Injections { get; } = [];

        internal InjectionTracker(string pipelineName)
        {
            _pipelineName = pipelineName;
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == ResilionTelemetry.MeterName
                        && instrument.Name == "resilion.chaos.injections")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? pipeline = null, strategy = null, operationKey = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case ResilionTelemetry.PipelineNameTag: pipeline = tag.Value as string; break;
                        case ResilionTelemetry.ChaosStrategyTag: strategy = tag.Value as string; break;
                        case ResilionTelemetry.OperationKeyTag: operationKey = tag.Value as string; break;
                    }
                }

                if (pipeline != _pipelineName)
                {
                    return;
                }

                lock (_gate)
                {
                    Injections.Add((strategy ?? "<none>", operationKey));
                }
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Injection_IncrementsChaosInjectionsCounter()
    {
        const string name = "chaos-telemetry-increments";
        using var tracker = new InjectionTracker(name);

        var pipeline = Pipeline.Create(b =>
        {
            b.Name = name;
            b.AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                FaultGenerator = _ => new InvalidOperationException("injected"),
            });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync(ct => new ValueTask<string>("real")));

        Assert.Single(tracker.Injections);
    }

    [Fact]
    public async Task Injection_CounterCarriesPipelineNameAndChaosStrategyTags()
    {
        const string name = "chaos-telemetry-tags";
        using var tracker = new InjectionTracker(name);

        var pipeline = Pipeline.Create(b =>
        {
            b.Name = name;
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                Latency = TimeSpan.Zero,
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));

        var injection = Assert.Single(tracker.Injections);
        Assert.Equal("latency", injection.Strategy);
    }

    [Fact]
    public async Task Injection_EachStrategyReportsItsOwnName()
    {
        const string name = "chaos-telemetry-strategy-names";
        using var tracker = new InjectionTracker(name);

        var pipeline = Pipeline.Create(b =>
        {
            b.Name = name;
            b.SuppressOrderingWarnings = true;
            b.AddChaosBehavior(new ChaosBehaviorStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                Behavior = _ => default,
            });
            b.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                Latency = TimeSpan.Zero,
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));

        Assert.Equal(
            new[] { "behavior", "latency" },
            tracker.Injections.Select(i => i.Strategy).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task NoInjection_DoesNotIncrementCounter()
    {
        const string name = "chaos-telemetry-no-injection";
        using var tracker = new InjectionTracker(name);

        var pipeline = Pipeline.Create(b =>
        {
            b.Name = name;
            b.AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 1.0,
                FaultGenerator = _ => new InvalidOperationException("injected"),
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));

        Assert.Empty(tracker.Injections);
    }

    [Fact]
    public async Task FaultGeneratorReturningNull_DoesNotIncrementCounter()
    {
        // The injection decision was taken but produced nothing, so nothing was injected and
        // the counter must not claim otherwise.
        const string name = "chaos-telemetry-null-fault";
        using var tracker = new InjectionTracker(name);

        var pipeline = Pipeline.Create(b =>
        {
            b.Name = name;
            b.AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = 1.0,
                Randomizer = () => 0.0,
                FaultGenerator = _ => null,
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<string>("real"));

        Assert.Empty(tracker.Injections);
    }
}
