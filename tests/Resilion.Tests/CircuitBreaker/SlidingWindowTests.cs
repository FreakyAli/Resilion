using Microsoft.Extensions.Time.Testing;
using Resilion.Internal;
using Xunit;

namespace Resilion.Tests;

/// <summary>
/// Direct tests for <see cref="SlidingWindow"/> — previously only exercised indirectly through
/// the circuit breaker strategies. <c>Resilion.Internal</c> types are visible here via
/// <c>InternalsVisibleTo</c>.
/// </summary>
public class SlidingWindowTests
{
    private static SlidingWindow Create(FakeTimeProvider timeProvider, TimeSpan? samplingDuration = null)
        => new(samplingDuration ?? TimeSpan.FromSeconds(10), timeProvider);

    [Fact]
    public void InitialState_RatioIsZero_TotalCountIsZero()
    {
        var window = Create(new FakeTimeProvider());

        var ratio = window.GetFailureRatio(out var totalCount);

        Assert.Equal(0.0, ratio);
        Assert.Equal(0, totalCount);
    }

    [Fact]
    public void AllFailures_RatioIsOne()
    {
        var window = Create(new FakeTimeProvider());

        for (var i = 0; i < 5; i++)
        {
            window.RecordFailure();
        }

        var ratio = window.GetFailureRatio(out var totalCount);

        Assert.Equal(1.0, ratio);
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public void AllSuccesses_RatioIsZero()
    {
        var window = Create(new FakeTimeProvider());

        for (var i = 0; i < 5; i++)
        {
            window.RecordSuccess();
        }

        var ratio = window.GetFailureRatio(out var totalCount);

        Assert.Equal(0.0, ratio);
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public void MixedOutcomes_RatioReflectsAccurateProportion()
    {
        var window = Create(new FakeTimeProvider());

        for (var i = 0; i < 3; i++)
        {
            window.RecordFailure();
        }

        for (var i = 0; i < 7; i++)
        {
            window.RecordSuccess();
        }

        var ratio = window.GetFailureRatio(out var totalCount);

        Assert.Equal(0.3, ratio, precision: 10);
        Assert.Equal(10, totalCount);
    }

    [Fact]
    public void RecordAndGetRatio_RecordsAndReadsUnderOneAcquisition()
    {
        var window = Create(new FakeTimeProvider());

        window.RecordAndGetRatio(isFailure: true, out var afterFirst);
        Assert.Equal(1, afterFirst);

        var ratio = window.RecordAndGetRatio(isFailure: false, out var afterSecond);

        Assert.Equal(2, afterSecond);
        Assert.Equal(0.5, ratio, precision: 10);
    }

    [Fact]
    public void BucketRotation_AdvancingPastOneBucket_KeepsOlderDataUntilWindowExpires()
    {
        var fakeTime = new FakeTimeProvider();
        // 10 buckets over a 10s window => 1s per bucket.
        var window = Create(fakeTime, TimeSpan.FromSeconds(10));

        window.RecordFailure();

        // Advance past a single bucket duration but well within the full window.
        fakeTime.Advance(TimeSpan.FromSeconds(1.5));
        window.RecordSuccess();

        var ratio = window.GetFailureRatio(out var totalCount);

        // Both the older failure and the newer success should still be visible.
        Assert.Equal(2, totalCount);
        Assert.Equal(0.5, ratio, precision: 10);
    }

    [Fact]
    public void FullWindowExpiry_AdvancingPastEntireDuration_ClearsAllData()
    {
        var fakeTime = new FakeTimeProvider();
        var window = Create(fakeTime, TimeSpan.FromSeconds(10));

        for (var i = 0; i < 5; i++)
        {
            window.RecordFailure();
        }

        // Advance past the entire sampling duration.
        fakeTime.Advance(TimeSpan.FromSeconds(15));

        var ratio = window.GetFailureRatio(out var totalCount);

        Assert.Equal(0.0, ratio);
        Assert.Equal(0, totalCount);
    }

    [Fact]
    public void Reset_ClearsAllRecordedData()
    {
        var window = Create(new FakeTimeProvider());

        for (var i = 0; i < 5; i++)
        {
            window.RecordFailure();
        }

        window.Reset();

        var ratio = window.GetFailureRatio(out var totalCount);
        Assert.Equal(0.0, ratio);
        Assert.Equal(0, totalCount);
    }

    /// <summary>
    /// Thread-safety regression: concurrent <see cref="SlidingWindow.RecordAndGetRatio"/> calls
    /// (the method that fixed the typed circuit breaker's race condition, see future-plans #1)
    /// must never throw and must always return a ratio within [0.0, 1.0].
    /// </summary>
    [Fact]
    public async Task RecordAndGetRatio_ConcurrentCallers_NeverThrowsAndRatioStaysInRange()
    {
        var window = Create(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var ratios = new System.Collections.Concurrent.ConcurrentBag<double>();

        var tasks = Enumerable.Range(0, 2000).Select(i => Task.Run(() =>
        {
            var ratio = window.RecordAndGetRatio(isFailure: i % 3 == 0, out _);
            ratios.Add(ratio);
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(2000, ratios.Count);
        Assert.All(ratios, r => Assert.InRange(r, 0.0, 1.0));
    }
}
