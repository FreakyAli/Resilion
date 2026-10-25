using Xunit;

namespace Resilion.Tests;

/// <summary>
/// <see cref="PredicateBuilder{TResult}"/> and <see cref="PredicateBuilder"/> — future-plans #43.
/// </summary>
public class PredicateBuilderTests
{
    private class CustomException : Exception
    {
        public CustomException(string message = "custom", Exception? inner = null)
            : base(message, inner) { }
    }

    private sealed class DerivedCustomException : CustomException;

    // ─── Exception clauses ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Handle_MatchingExceptionType_ReturnsTrue()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().Handle<CustomException>();

        Assert.True(predicate(Outcome<int>.FromException(new CustomException())));
    }

    [Fact]
    public void Handle_NonMatchingExceptionType_ReturnsFalse()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().Handle<CustomException>();

        Assert.False(predicate(Outcome<int>.FromException(new InvalidOperationException())));
    }

    [Fact]
    public void Handle_DerivedExceptionType_Matches()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().Handle<CustomException>();

        Assert.True(predicate(Outcome<int>.FromException(new DerivedCustomException())));
    }

    [Fact]
    public void Handle_WithPredicate_AppliesPredicate()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>()
            .Handle<CustomException>(ex => ex.Message == "yes");

        Assert.True(predicate(Outcome<int>.FromException(new CustomException("yes"))));
        Assert.False(predicate(Outcome<int>.FromException(new CustomException("no"))));
    }

    [Fact]
    public void Handle_OnSuccessOutcome_ReturnsFalse()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().Handle<Exception>();

        Assert.False(predicate(Outcome<int>.FromResult(1)));
    }

    [Fact]
    public void HandleInner_NestedException_Matches()
    {
        var wrapped = new InvalidOperationException("outer", new CustomException());
        var builder = new PredicateBuilder<int>().HandleInner<CustomException>();
        Func<Outcome<int>, bool> predicate = builder;

        Assert.True(predicate(Outcome<int>.FromException(wrapped)));
    }

    [Fact]
    public void HandleInner_AggregateException_FlattensAndMatches()
    {
        var aggregate = new AggregateException(new InvalidOperationException(), new CustomException());
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().HandleInner<CustomException>();

        Assert.True(predicate(Outcome<int>.FromException(aggregate)));
    }

    [Fact]
    public void HandleInner_SelfReferencingChain_DoesNotHang()
    {
        // Malformed but observable in the wild: a bounded walk must terminate rather than spin.
        var a = new CustomException("a");
        var wrapper = new InvalidOperationException("w", a);
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().HandleInner<TimeoutException>();

        Assert.False(predicate(Outcome<int>.FromException(wrapper)));
    }

    [Fact]
    public void Handle_DoesNotInheritTheDefaultCancellationCarveOut()
    {
        // The documented difference from the null-ShouldHandle default, which excludes OCE.
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().Handle<Exception>();

        Assert.True(predicate(Outcome<int>.FromException(new OperationCanceledException())));
    }

    // ─── Result clauses ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void HandleResult_WithPredicate_MatchesOnResult()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().HandleResult(r => r > 10);

        Assert.True(predicate(Outcome<int>.FromResult(11)));
        Assert.False(predicate(Outcome<int>.FromResult(10)));
    }

    [Fact]
    public void HandleResult_WithValue_UsesDefaultEqualityComparer()
    {
        Func<Outcome<string>, bool> predicate = new PredicateBuilder<string>().HandleResult("fail");

        Assert.True(predicate(Outcome<string>.FromResult("fail")));
        Assert.False(predicate(Outcome<string>.FromResult("ok")));
    }

    [Fact]
    public void HandleResult_OnFailureOutcome_ReturnsFalse()
    {
        // Result clauses must never observe a default value standing in for a failure.
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>().HandleResult(0);

        Assert.False(predicate(Outcome<int>.FromException(new CustomException())));
    }

    // ─── Composition and Build semantics ────────────────────────────────────────────────────

    [Fact]
    public void MultipleClauses_CombineWithOr()
    {
        Func<Outcome<int>, bool> predicate = new PredicateBuilder<int>()
            .Handle<CustomException>()
            .HandleResult(r => r < 0);

        Assert.True(predicate(Outcome<int>.FromException(new CustomException())));
        Assert.True(predicate(Outcome<int>.FromResult(-1)));
        Assert.False(predicate(Outcome<int>.FromResult(1)));
        Assert.False(predicate(Outcome<int>.FromException(new InvalidOperationException())));
    }

    [Fact]
    public void Build_WithNoClauses_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new PredicateBuilder<int>().Build());

        Assert.Contains("no clauses", ex.Message);
    }

    [Fact]
    public void ImplicitConversion_WithNoClauses_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Func<Outcome<int>, bool> _ = new PredicateBuilder<int>();
        });
    }

    [Fact]
    public void Build_SnapshotsClauses_LaterMutationDoesNotAffectReturnedDelegate()
    {
        var builder = new PredicateBuilder<int>().HandleResult(1);
        var predicate = builder.Build();

        builder.HandleResult(2);

        Assert.True(predicate(Outcome<int>.FromResult(1)));
        Assert.False(predicate(Outcome<int>.FromResult(2)));
    }

    [Fact]
    public void Build_CalledTwice_ReturnsIndependentWorkingDelegates()
    {
        // Deliberately not single-use, unlike PipelineBuilder.
        var builder = new PredicateBuilder<int>().HandleResult(1);

        var first = builder.Build();
        var second = builder.Build();

        Assert.True(first(Outcome<int>.FromResult(1)));
        Assert.True(second(Outcome<int>.FromResult(1)));
    }

    [Fact]
    public void Handle_NullPredicate_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new PredicateBuilder<int>().Handle<Exception>(null!));

    // ─── Non-generic builder ────────────────────────────────────────────────────────────────

    [Fact]
    public void NonGeneric_Handle_ProducesFuncOfException()
    {
        Func<Exception, bool> predicate = new PredicateBuilder().Handle<CustomException>();

        Assert.True(predicate(new CustomException()));
        Assert.False(predicate(new InvalidOperationException()));
    }

    [Fact]
    public void NonGeneric_HandleInner_WalksChain()
    {
        Func<Exception, bool> predicate = new PredicateBuilder().HandleInner<CustomException>();

        Assert.True(predicate(new InvalidOperationException("outer", new CustomException())));
    }

    [Fact]
    public void NonGeneric_MultipleClauses_CombineWithOr()
    {
        Func<Exception, bool> predicate = new PredicateBuilder()
            .Handle<CustomException>()
            .Handle<TimeoutException>();

        Assert.True(predicate(new CustomException()));
        Assert.True(predicate(new TimeoutException()));
        Assert.False(predicate(new InvalidOperationException()));
    }

    [Fact]
    public void NonGeneric_Build_WithNoClauses_Throws()
        => Assert.Throws<InvalidOperationException>(() => new PredicateBuilder().Build());

    // ─── Works where it is meant to be used ─────────────────────────────────────────────────

    [Fact]
    public async Task Async_TypedPipelineWithPredicateBuilder_RetriesAsExpected()
    {
        var attempts = 0;
        var pipeline = Pipeline.Create<string>(b => b.AddRetry(new RetryStrategyOptions<string>
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
            ShouldHandle = new PredicateBuilder<string>().HandleResult("fail"),
        }));

        var result = await pipeline.ExecuteAsync(
            ct => new ValueTask<string>(++attempts < 3 ? "fail" : "ok"));

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Async_UntypedPipelineWithNonGenericPredicateBuilder_RetriesAsExpected()
    {
        var attempts = 0;
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay.None,
            ShouldHandle = new PredicateBuilder().Handle<CustomException>(),
        }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            if (++attempts < 3)
            {
                throw new CustomException();
            }

            return new ValueTask<string>("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }
}
