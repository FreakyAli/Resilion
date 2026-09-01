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
