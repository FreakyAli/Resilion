using Xunit;

namespace Resilion.Tests;

public class OutcomeTests
{
    [Fact]
    public void FromResult_CreatesSuccessfulOutcome()
    {
        var outcome = Outcome<string>.FromResult("hello");

        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.IsFailure);
        Assert.Equal("hello", outcome.Result);
        Assert.Null(outcome.Exception);
    }

    [Fact]
    public void FromException_CreatesFailedOutcome()
    {
        var ex = new InvalidOperationException("test");
        var outcome = Outcome<string>.FromException(ex);

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsFailure);
        Assert.Same(ex, outcome.Exception);
    }

    [Fact]
    public void Result_ThrowsOnFailedOutcome()
    {
        var ex = new InvalidOperationException("test");
        var outcome = Outcome<string>.FromException(ex);

        var thrown = Assert.Throws<InvalidOperationException>(() => outcome.Result);
        Assert.Contains("Cannot access Result", thrown.Message);
    }

    [Fact]
    public void TryGetResult_ReturnsTrueOnSuccess()
    {
        var outcome = Outcome<int>.FromResult(42);

        Assert.True(outcome.TryGetResult(out var result));
        Assert.Equal(42, result);
    }

    [Fact]
    public void TryGetResult_ReturnsFalseOnFailure()
    {
        var outcome = Outcome<int>.FromException(new Exception("fail"));

        Assert.False(outcome.TryGetResult(out var result));
        Assert.Equal(default, result);
    }

    [Fact]
    public void GetResultOrDefault_ReturnsResultOnSuccess()
    {
        var outcome = Outcome<string>.FromResult("value");

        Assert.Equal("value", outcome.GetResultOrDefault("fallback"));
    }

    [Fact]
    public void GetResultOrDefault_ReturnsDefaultOnFailure()
    {
        var outcome = Outcome<string>.FromException(new Exception());

        Assert.Equal("fallback", outcome.GetResultOrDefault("fallback"));
    }

    [Fact]
    public void ThrowIfFailed_ReturnsResultOnSuccess()
    {
        var outcome = Outcome<int>.FromResult(99);

        Assert.Equal(99, outcome.ThrowIfFailed());
    }

    [Fact]
    public void ThrowIfFailed_PreservesStackTrace()
    {
        Exception captured;
        try
        {
            throw new InvalidOperationException("original");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        var outcome = Outcome<int>.FromException(captured);

        var thrown = Assert.Throws<InvalidOperationException>(() => outcome.ThrowIfFailed());
        Assert.Equal("original", thrown.Message);
        // The stack trace should contain the original throw site
        Assert.Contains("ThrowIfFailed_PreservesStackTrace", thrown.StackTrace!);
    }

    [Fact]
    public void Match_CallsOnSuccessForSuccessfulOutcome()
    {
        var outcome = Outcome<string>.FromResult("hello");

        var result = outcome.Match(
            onSuccess: s => s.Length,
            onFailure: _ => -1);

        Assert.Equal(5, result);
    }

    [Fact]
    public void Match_CallsOnFailureForFailedOutcome()
    {
        var outcome = Outcome<string>.FromException(new InvalidOperationException("fail"));

        var result = outcome.Match(
            onSuccess: s => s.Length,
            onFailure: ex => ex.Message.Length);

        Assert.Equal(4, result);
    }

    [Fact]
    public void ImplicitConversion_CreatesSuccessfulOutcome()
    {
        Outcome<string> outcome = "implicit";

        Assert.True(outcome.IsSuccess);
        Assert.Equal("implicit", outcome.Result);
    }

    [Fact]
    public void FromException_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => Outcome<string>.FromException(null!));
    }

    [Fact]
    public void Equality_SuccessfulOutcomes()
    {
        var a = Outcome<int>.FromResult(42);
        var b = Outcome<int>.FromResult(42);
        var c = Outcome<int>.FromResult(99);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void Equality_FailedOutcomes_SameException()
    {
        var ex = new Exception("test");
        var a = Outcome<int>.FromException(ex);
        var b = Outcome<int>.FromException(ex);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_SuccessNotEqualToFailure()
    {
        var success = Outcome<int>.FromResult(0);
        var failure = Outcome<int>.FromException(new Exception());

        Assert.NotEqual(success, failure);
    }

    [Fact]
    public void ToString_ShowsSuccessInfo()
    {
        var outcome = Outcome<string>.FromResult("test");

        Assert.Contains("Success", outcome.ToString());
        Assert.Contains("test", outcome.ToString());
    }

    [Fact]
    public void ToString_ShowsFailureInfo()
    {
        var outcome = Outcome<string>.FromException(new InvalidOperationException("oops"));

        Assert.Contains("Failure", outcome.ToString());
        Assert.Contains("InvalidOperationException", outcome.ToString());
        Assert.Contains("oops", outcome.ToString());
    }
}
