using System.Diagnostics;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// ActivitySource span coverage — future-plans #56.
/// </summary>
/// <remarks>
/// Spans previously existed only on the non-generic strategies, so any pipeline built with
/// <c>Pipeline.Create&lt;TResult&gt;</c> emitted no retry or circuit-breaker spans at all while
/// <c>docs/telemetry.md</c> promised that every strategy execution creates one. The parity test
/// at the bottom is the one that would have caught that, and it is what makes the later retry-loop
/// deduplication (#27) safe to attempt.
/// </remarks>
public class ActivityTests
{
    /// <summary>
    /// An <see cref="ActivityListener"/> with <c>Sample => AllData</c> is mandatory: without a
    /// listener <c>StartActivity</c> returns null and every assertion here would pass vacuously.
    /// </summary>
    private sealed class ActivityTracker : IDisposable
    {
        private readonly ActivityListener _listener;

        public List<Activity> Stopped { get; } = [];

        /// <param name="pipelineName">
        /// Only spans tagged with this pipeline name are recorded. <see cref="ActivitySource"/>
        /// listeners are process-global and xunit runs test classes in parallel, so an unfiltered
        /// tracker also sees spans emitted by other tests. Every pipeline built in this class is
        /// named for exactly this reason.
        /// </param>
        public ActivityTracker(string pipelineName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == ResilionTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = a =>
                {
                    var name = a.Tags.FirstOrDefault(t => t.Key == ResilionTelemetry.PipelineNameTag).Value;
                    if (name != pipelineName)
                    {
                        return;
                    }

                    lock (Stopped) { Stopped.Add(a); }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<string> Names()
        {
            lock (Stopped) { return Stopped.Select(a => a.OperationName).OrderBy(n => n).ToList(); }
        }

        public string? TagOf(string span, string tag)
        {
            lock (Stopped)
            {
                return Stopped.First(a => a.OperationName == span)
                    .Tags.FirstOrDefault(t => t.Key == tag).Value;
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    // ─── Typed pipelines emit spans (the #56 gap) ───────────────────────────────────────────

    [Fact]
    public async Task Async_TypedRetryPipeline_EmitsRetrySpan()
    {
        using var tracker = new ActivityTracker("typed-retry-async");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "typed-retry-async";
            b.AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
                ShouldHandle = o => o.TryGetResult(out var r) && r == 0,
            });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<int>(0));

        Assert.Contains("Retry", tracker.Names());
    }

    [Fact]
    public void Sync_TypedRetryPipeline_EmitsRetrySpan()
    {
        using var tracker = new ActivityTracker("typed-retry-sync");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "typed-retry-sync";
            b.AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
                ShouldHandle = o => o.TryGetResult(out var r) && r == 0,
            });
        });

        pipeline.Execute(ct => 0);

        Assert.Contains("Retry", tracker.Names());
    }

    [Fact]
    public async Task Async_TypedCircuitBreakerPipeline_EmitsCircuitBreakerSpan()
    {
        using var tracker = new ActivityTracker("typed-cb-async");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "typed-cb-async";
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int> { MinimumThroughput = 100 });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));

        Assert.Contains("CircuitBreaker", tracker.Names());
    }

    [Fact]
    public void Sync_TypedCircuitBreakerPipeline_EmitsCircuitBreakerSpan()
    {
        using var tracker = new ActivityTracker("typed-cb-sync");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "typed-cb-sync";
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int> { MinimumThroughput = 100 });
        });

        pipeline.Execute(ct => 1);

        Assert.Contains("CircuitBreaker", tracker.Names());
    }

    [Fact]
    public void Sync_HedgingPipeline_EmitsHedgingSpan()
    {
        using var tracker = new ActivityTracker("sync-hedging");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "sync-hedging";
            b.AddHedging(new HedgingStrategyOptions<int>
            {
                MaxHedgedAttempts = 2,
                HedgingDelay = System.Threading.Timeout.InfiniteTimeSpan,
            });
        });

        pipeline.Execute(ct => 1);

        Assert.Contains("Hedging", tracker.Names());
    }

    // ─── Tag shape ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllStrategySpans_CarryStrategyNamePipelineNameAndOperationKeyTags()
    {
        using var tracker = new ActivityTracker("tag-shape");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "tag-shape";
            b.AddTimeout(TimeSpan.FromSeconds(30));
            b.AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 0 });
        });

        // ExecuteOutcomeAsync takes a caller-owned context, which is the only way to set
        // OperationKey — and asserting on it is the point of this test.
        var context = ResilienceContextPool.Shared.Rent();
        try
        {
            context.OperationKey = "tag-shape-op";
            await pipeline.ExecuteOutcomeAsync(
                static (state, ctx) => new ValueTask<Outcome<int>>(Outcome<int>.FromResult(1)),
                0,
                context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        foreach (var span in new[] { "Timeout", "Retry" })
        {
            Assert.Equal(span, tracker.TagOf(span, "strategy.name"));
            Assert.Equal("tag-shape", tracker.TagOf(span, ResilionTelemetry.PipelineNameTag));
            Assert.Equal("tag-shape-op", tracker.TagOf(span, ResilionTelemetry.OperationKeyTag));
        }
    }

    [Fact]
    public async Task SpanOutcomeTag_MatchesDocumentedVocabulary()
    {
        // Keeps docs/telemetry.md and the code married. Any new outcome value must be documented.
        var documented = new HashSet<string>
        {
            "success", "failure", "exception", "timeout", "no_timeout",
            "rejected", "retry_exhausted", "fallback_applied", "hedging_exhausted",
        };

        using var tracker = new ActivityTracker("outcome-vocab");
        var pipeline = Pipeline.Create<int>(b =>
        {
            b.Name = "outcome-vocab";
            b.AddTimeout(TimeSpan.FromSeconds(30));
            b.AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = RetryDelay.None,
                ShouldHandle = o => o.TryGetResult(out var r) && r == 0,
            });
            b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int> { MinimumThroughput = 100 });
        });

        await pipeline.ExecuteAsync(ct => new ValueTask<int>(0));

        var observed = tracker.Stopped
            .SelectMany(a => a.Tags.Where(t => t.Key == "outcome"))
            .Select(t => t.Value!)
            .ToList();

        Assert.NotEmpty(observed);
        foreach (var o in observed)
        {
            Assert.Contains(o, documented);
        }
    }

    // ─── The parity test that would have caught #56 ─────────────────────────────────────────

    [Fact]
    public async Task TypedAndUntypedPipelines_EmitTheSameSpanNames()
    {
        // Same strategy set through both factories must produce the same spans. This is the
        // invariant that was silently false: typed retry and typed circuit breaker emitted none.
        IReadOnlyList<string> untyped;
        using (var tracker = new ActivityTracker("parity-untyped"))
        {
            var pipeline = Pipeline.Create(b =>
            {
                b.Name = "parity-untyped";
                b.AddTimeout(TimeSpan.FromSeconds(30));
                b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 0 });
                b.AddCircuitBreaker(new CircuitBreakerStrategyOptions { MinimumThroughput = 100 });
            });
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));
            untyped = tracker.Names();
        }

        IReadOnlyList<string> typed;
        using (var tracker = new ActivityTracker("parity-typed"))
        {
            var pipeline = Pipeline.Create<int>(b =>
            {
                b.Name = "parity-typed";
                b.AddTimeout(TimeSpan.FromSeconds(30));
                b.AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 0 });
                b.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int> { MinimumThroughput = 100 });
            });
            await pipeline.ExecuteAsync(ct => new ValueTask<int>(1));
            typed = tracker.Names();
        }

        Assert.Equal(untyped, typed);
    }
}
