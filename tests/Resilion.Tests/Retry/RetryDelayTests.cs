using Xunit;

namespace Resilion.Tests;

public class RetryDelayTests
{
    [Fact]
    public void Constant_NegativeDelay_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RetryDelay.Constant(TimeSpan.FromMilliseconds(-100)));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constant_ZeroDelay_Succeeds()
    {
        var delay = RetryDelay.Constant(TimeSpan.Zero);
        Assert.NotNull(delay);
    }

    [Fact]
    public void Constant_PositiveDelay_Succeeds()
    {
        var delay = RetryDelay.Constant(TimeSpan.FromMilliseconds(100));
        Assert.NotNull(delay);
    }

    [Fact]
    public void Linear_NegativeBaseDelay_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RetryDelay.Linear(TimeSpan.FromMilliseconds(-100)));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Linear_NegativeMaxDelay_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RetryDelay.Linear(TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromMilliseconds(-50)));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Linear_ValidBaseDelayWithoutMaxDelay_Succeeds()
    {
        var delay = RetryDelay.Linear(TimeSpan.FromSeconds(1));
        Assert.NotNull(delay);
    }

    [Fact]
    public void Linear_ValidBaseDelayWithValidMaxDelay_Succeeds()
    {
        var delay = RetryDelay.Linear(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30));
        Assert.NotNull(delay);
    }

    [Fact]
    public void Exponential_NegativeBaseDelay_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RetryDelay.Exponential(TimeSpan.FromMilliseconds(-100)));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exponential_NegativeMaxDelay_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RetryDelay.Exponential(TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromMilliseconds(-50)));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exponential_ValidBaseDelayWithoutMaxDelay_Succeeds()
    {
        var delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100));
        Assert.NotNull(delay);
    }

    [Fact]
    public void Exponential_ValidBaseDelayWithValidMaxDelay_Succeeds()
    {
        var delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromSeconds(60));
        Assert.NotNull(delay);
    }
}
