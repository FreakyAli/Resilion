using Resilion.Internal;
using Xunit;

namespace Resilion.Tests;

public class PipelineTests
{
    // ──────────────────────────────────────────────────────────────────
    // Empty pipeline
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_ExecuteAsync_PassesThrough()
    {
        var result = await Pipeline.Empty.ExecuteAsync(static ct => new ValueTask<string>("hello"));
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Empty_Execute_PassesThrough()
    {
        var result = Pipeline.Empty.Execute(static ct => "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Empty_VoidExecuteAsync_Completes()
    {
        var executed = false;
        await Pipeline.Empty.ExecuteAsync(ct =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        });
        Assert.True(executed);
    }

    [Fact]
    public void Empty_VoidExecute_Completes()
    {
        var executed = false;
        Pipeline.Empty.Execute(ct => { executed = true; });
        Assert.True(executed);
    }

    // ──────────────────────────────────────────────────────────────────
    // Strategy chain execution
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleStrategy_WrapsExecution()
    {
        var log = new List<string>();

        var pipeline = Pipeline.Create(b => b.AddStrategy(new LogStrategy(log, "S1")));

        var result = await pipeline.ExecuteAsync(static ct => new ValueTask<string>("result"));

        Assert.Equal("result", result);
        Assert.Equal(["S1:before", "S1:after"], log);
    }

    [Fact]
    public async Task MultipleStrategies_ExecuteOuterToInner()
    {
        var log = new List<string>();

        var pipeline = Pipeline.Create(b => b
            .AddStrategy(new LogStrategy(log, "Outer"))
            .AddStrategy(new LogStrategy(log, "Inner")));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            log.Add("callback");
            return new ValueTask<string>("result");
        });

        Assert.Equal("result", result);
        Assert.Equal(["Outer:before", "Inner:before", "callback", "Inner:after", "Outer:after"], log);
    }

    [Fact]
    public void MultipleStrategies_ExecuteOuterToInner_Sync()
    {
        var log = new List<string>();

        var pipeline = Pipeline.Create(b => b
            .AddStrategy(new LogStrategy(log, "Outer"))
            .AddStrategy(new LogStrategy(log, "Inner")));

        var result = pipeline.Execute(ct =>
        {
            log.Add("callback");
            return "result";
        });

        Assert.Equal("result", result);
        Assert.Equal(["Outer:before", "Inner:before", "callback", "Inner:after", "Outer:after"], log);
    }

    // ──────────────────────────────────────────────────────────────────
    // State parameter (closure-free execution)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StateParameter_AvoidsClosure()
    {
        var pipeline = Pipeline.Empty;

        var result = await pipeline.ExecuteAsync(
            static (state, ct) => new ValueTask<int>(state.a + state.b),
            (a: 3, b: 7));

        Assert.Equal(10, result);
    }

    [Fact]
    public void StateParameter_Sync_AvoidsClosure()
    {
        var pipeline = Pipeline.Empty;

        var result = pipeline.Execute(
            static (state, ct) => state.a * state.b,
            (a: 6, b: 7));

        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Exception propagation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exception_PropagatesFromCallback()
    {
        var pipeline = Pipeline.Create(b => b.AddStrategy(new NoOpStrategy()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync<string>(ct => throw new InvalidOperationException("test")).AsTask());

        Assert.Equal("test", ex.Message);
    }

    [Fact]
    public void Exception_PropagatesFromCallback_Sync()
    {
        var pipeline = Pipeline.Create(b => b.AddStrategy(new NoOpStrategy()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            pipeline.Execute<string>(ct => throw new InvalidOperationException("test")));

        Assert.Equal("test", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────
    // Cancellation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationToken_FlowsToCallback()
    {
        var pipeline = Pipeline.Empty;
        using var cts = new CancellationTokenSource();

        CancellationToken received = default;
        await pipeline.ExecuteAsync(ct =>
        {
            received = ct;
            return new ValueTask<int>(0);
        }, cts.Token);

        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public async Task CancelledToken_PropagatesOperationCanceledException()
    {
        var pipeline = Pipeline.Create(b => b.AddStrategy(new NoOpStrategy()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync<int>(ct =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<int>(0);
            }, cts.Token).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────
    // Pipeline composition via AddPipeline
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPipeline_FlattensStrategies()
    {
        var log = new List<string>();

        var inner = Pipeline.Create(b => b.AddStrategy(new LogStrategy(log, "Inner")));
        var pipeline = Pipeline.Create(b => b
            .AddStrategy(new LogStrategy(log, "Outer"))
            .AddPipeline(inner));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            log.Add("callback");
            return new ValueTask<string>("done");
        });

        Assert.Equal("done", result);
        Assert.Equal(["Outer:before", "Inner:before", "callback", "Inner:after", "Outer:after"], log);
    }

    // ──────────────────────────────────────────────────────────────────
    // ExecuteOutcomeAsync (no-throw path)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteOutcomeAsync_ReturnsSuccessOutcome()
    {
        var pipeline = Pipeline.Empty;
        var context = ResilienceContextPool.Shared.Rent();
        try
        {
            var outcome = await pipeline.ExecuteOutcomeAsync(
                static (state, ctx) => new ValueTask<Outcome<string>>(Outcome<string>.FromResult("ok")),
                "state",
                context);

            Assert.True(outcome.IsSuccess);
            Assert.Equal("ok", outcome.Result);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task ExecuteOutcomeAsync_ReturnsFailureOutcome_WithoutThrowing()
    {
        var pipeline = Pipeline.Empty;
        var context = ResilienceContextPool.Shared.Rent();
        try
        {
            var outcome = await pipeline.ExecuteOutcomeAsync(
                static (state, ctx) => new ValueTask<Outcome<string>>(
                    Outcome<string>.FromException(new InvalidOperationException("fail"))),
                "state",
                context);

            Assert.True(outcome.IsFailure);
            Assert.IsType<InvalidOperationException>(outcome.Exception);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Typed pipeline
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypedPipeline_ExecutesWithTypedStrategy()
    {
        var pipeline = Pipeline.Create<string>(b => b
            .AddStrategy(new UpperCaseStrategy()));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<string>("hello"));

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void TypedPipeline_ExecutesSync()
    {
        var pipeline = Pipeline.Create<string>(b => b
            .AddStrategy(new UpperCaseStrategy()));

        var result = pipeline.Execute(ct => "world");

        Assert.Equal("WORLD", result);
    }

    [Fact]
    public async Task TypedPipeline_DelegateStrategy_Works()
    {
        var pipeline = Pipeline.Create<int>(b => b
            .AddStrategy("double-it", async (ctx, next) =>
            {
                var outcome = await next(ctx);
                if (outcome.TryGetResult(out var value))
                {
                    return Outcome<int>.FromResult(value * 2);
                }

                return outcome;
            }));

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(21));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TypedPipeline_MixesNonGenericAndTypedStrategies()
    {
        var log = new List<string>();

        var pipeline = Pipeline.Create<string>(b => b
            .AddStrategy(new LogStrategy(log, "NonGeneric"))
            .AddStrategy(new UpperCaseStrategy()));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            log.Add("callback");
            return new ValueTask<string>("hello");
        });

        Assert.Equal("HELLO", result);
        Assert.Equal(["NonGeneric:before", "callback", "NonGeneric:after"], log);
    }

    // ──────────────────────────────────────────────────────────────────
    // Pipeline.Create factory
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsEmptyWhenNoStrategies()
    {
        var pipeline = Pipeline.Create(b => { });

        var result = await pipeline.ExecuteAsync(ct => new ValueTask<int>(42));
        Assert.Equal(42, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>Logs before/after execution for verifying strategy ordering.</summary>
    private sealed class LogStrategy : Strategy
    {
        private readonly List<string> _log;
        private readonly string _name;

        public LogStrategy(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context)
        {
            _log.Add($"{_name}:before");
            var outcome = await callback(context).ConfigureAwait(false);
            _log.Add($"{_name}:after");
            return outcome;
        }

        protected internal override Outcome<TResult> Execute<TResult>(
            Func<ResilienceContext, Outcome<TResult>> callback,
            ResilienceContext context)
        {
            _log.Add($"{_name}:before");
            var outcome = callback(context);
            _log.Add($"{_name}:after");
            return outcome;
        }

        public override void Dispose() => _log.Add($"{_name}:disposed");

        public override ValueTask DisposeAsync()
        {
            _log.Add($"{_name}:disposedAsync");
            return default;
        }
    }

    /// <summary>A no-op strategy that passes through to the next component.</summary>
    private sealed class NoOpStrategy : Strategy
    {
        protected internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context)
            => callback(context);

        protected internal override Outcome<TResult> Execute<TResult>(
            Func<ResilienceContext, Outcome<TResult>> callback,
            ResilienceContext context)
            => callback(context);
    }

    /// <summary>A typed strategy that uppercases string results.</summary>
    private sealed class UpperCaseStrategy : Strategy<string>
    {
        protected internal override async ValueTask<Outcome<string>> ExecuteAsync(
            Func<ResilienceContext, ValueTask<Outcome<string>>> callback,
            ResilienceContext context)
        {
            var outcome = await callback(context).ConfigureAwait(false);
            if (outcome.TryGetResult(out var value))
            {
                return Outcome<string>.FromResult(value.ToUpperInvariant());
            }

            return outcome;
        }

        protected internal override Outcome<string> Execute(
            Func<ResilienceContext, Outcome<string>> callback,
            ResilienceContext context)
        {
            var outcome = callback(context);
            if (outcome.TryGetResult(out var value))
            {
                return Outcome<string>.FromResult(value.ToUpperInvariant());
            }

            return outcome;
        }
    }
}

/// <summary>
/// Guards against using a builder after <c>Build()</c> has already been called on it —
/// previously silent (strategies added after Build() were just lost), now throws.
/// </summary>
public class PipelineBuilderPostBuildGuardTests
{
    [Fact]
    public void NonGeneric_AddStrategyAfterBuild_Throws()
    {
        var builder = new PipelineBuilder();
        builder.AddRetry();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AddRetry());
    }

    [Fact]
    public void NonGeneric_AddPipelineAfterBuild_Throws()
    {
        var builder = new PipelineBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AddPipeline(Pipeline.Empty));
    }

    [Fact]
    public void NonGeneric_BuildTwice_Throws()
    {
        var builder = new PipelineBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Typed_AddStrategyAfterBuild_Throws()
    {
        var builder = new PipelineBuilder<string>();
        builder.AddRetry();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AddRetry());
    }

    [Fact]
    public void Typed_AddDelegateStrategyAfterBuild_Throws()
    {
        var builder = new PipelineBuilder<string>();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddStrategy("custom", (ctx, next) => next(ctx)));
    }

    [Fact]
    public void Typed_BuildTwice_Throws()
    {
        var builder = new PipelineBuilder<string>();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}

/// <summary>
/// <see cref="TypedStrategyComponent{TStrategyResult}"/> silently skips a typed strategy when
/// executed with a mismatched result type — this exercises that path directly (via
/// <c>InternalsVisibleTo</c>) and verifies it now at least warns via <c>Debug.WriteLine</c>.
/// </summary>
public class TypedStrategyComponentMismatchTests
{
    private sealed class PassthroughStringStrategy : Strategy<string>
    {
        public bool WasCalled { get; private set; }

        protected internal override ValueTask<Outcome<string>> ExecuteAsync(
            Func<ResilienceContext, ValueTask<Outcome<string>>> callback,
            ResilienceContext context)
        {
            WasCalled = true;
            return callback(context);
        }
    }

    private sealed class RecordingTraceListener : System.Diagnostics.TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) => Messages.Add(message ?? string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_TypeMismatch_SkipsStrategyAndWarns()
    {
        var strategy = new PassthroughStringStrategy();
        var component = new TypedStrategyComponent<string>(strategy, PipelineComponent.Empty);

        var listener = new RecordingTraceListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            // Executed with TResult = int, but the strategy is Strategy<string> — mismatch.
            var outcome = await component.ExecuteAsync<int>(
                ctx => new ValueTask<Outcome<int>>(Outcome<int>.FromResult(42)),
                ResilienceContextPool.Shared.Rent());

            // Skipped, not applied — the callback's result passes through untouched.
            Assert.Equal(42, outcome.Result);
            Assert.False(strategy.WasCalled);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }

#if DEBUG
        Assert.Contains(listener.Messages, m => m.Contains("was skipped, not applied"));
#endif
    }
}

/// <summary>
/// <c>IAsyncDisposable</c> on <see cref="Pipeline"/> — enables <c>await using</c> and walks the
/// component chain asynchronously, calling each strategy's <c>DisposeAsync()</c>.
/// </summary>
public class PipelineAsyncDisposableTests
{
    private sealed class DisposeTrackingStrategy : Strategy
    {
        private readonly List<string> _log;
        private readonly string _name;

        public DisposeTrackingStrategy(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        protected internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context)
            => callback(context);

        public override ValueTask DisposeAsync()
        {
            _log.Add($"{_name}:disposedAsync");
            return default;
        }
    }

    [Fact]
    public async Task AwaitUsing_NonGenericPipeline_CallsDisposeAsyncOnEachStrategy()
    {
        var log = new List<string>();

        await using (var pipeline = Pipeline.Create(b => b
            .AddStrategy(new DisposeTrackingStrategy(log, "Outer"))
            .AddStrategy(new DisposeTrackingStrategy(log, "Inner"))))
        {
            await pipeline.ExecuteAsync(static ct => new ValueTask<string>("ok"));
        }

        Assert.Equal(["Outer:disposedAsync", "Inner:disposedAsync"], log);
    }

    private sealed class DisposeTrackingTypedStrategy : Strategy<string>
    {
        private readonly List<string> _log;
        private readonly string _name;

        public DisposeTrackingTypedStrategy(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        protected internal override ValueTask<Outcome<string>> ExecuteAsync(
            Func<ResilienceContext, ValueTask<Outcome<string>>> callback,
            ResilienceContext context)
            => callback(context);

        public override ValueTask DisposeAsync()
        {
            _log.Add($"{_name}:disposedAsync");
            return default;
        }
    }

    [Fact]
    public async Task AwaitUsing_TypedPipeline_CallsDisposeAsyncOnEachStrategy()
    {
        var log = new List<string>();

        await using (var pipeline = Pipeline.Create<string>(b =>
            b.AddStrategy(new DisposeTrackingTypedStrategy(log, "Only"))))
        {
            await pipeline.ExecuteAsync(static ct => new ValueTask<string>("ok"));
        }

        Assert.Equal(["Only:disposedAsync"], log);
    }

    private sealed class SyncOnlyDisposeStrategy : Strategy
    {
        private readonly List<string> _log;

        public SyncOnlyDisposeStrategy(List<string> log) => _log = log;

        protected internal override ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
            Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context)
            => callback(context);

        public override void Dispose() => _log.Add("SyncOnly:disposed");
    }

    [Fact]
    public async Task DisposeAsync_DefaultImplementation_FallsBackToSyncDispose()
    {
        // A strategy that only overrides the sync Dispose() — the base DisposeAsync() default
        // must still call it, so custom strategies written before IAsyncDisposable existed
        // keep working with `await using`.
        var log = new List<string>();
        var pipeline = Pipeline.Create(b => b.AddStrategy(new SyncOnlyDisposeStrategy(log)));

        await pipeline.DisposeAsync();

        Assert.Contains("SyncOnly:disposed", log);
    }
}
