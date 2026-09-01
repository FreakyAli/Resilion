namespace Resilion.Internal;

/// <summary>
/// A bucketed sliding window that tracks success/failure counts over a configurable duration.
/// Uses a fixed-size ring buffer of time buckets, each covering a fraction of the total window.
/// </summary>
/// <remarks>
/// Thread-safe. All public methods acquire the internal lock. Counter increments on the current
/// bucket use Interlocked for minimal contention on the hot path (Closed state, call succeeds).
/// </remarks>
internal sealed class SlidingWindow
{
    private const int BucketCount = 10;

    private readonly Bucket[] _buckets;
    private readonly TimeSpan _bucketDuration;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    private long _currentBucketStartTimestamp;
    private int _currentBucketIndex;

    internal SlidingWindow(TimeSpan samplingDuration, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _bucketDuration = samplingDuration / BucketCount;
        _buckets = new Bucket[BucketCount];
        for (var i = 0; i < BucketCount; i++)
        {
            _buckets[i] = new Bucket();
        }

        _currentBucketStartTimestamp = timeProvider.GetTimestamp();
        _currentBucketIndex = 0;
    }

    /// <summary>
    /// Records a successful execution.
    /// </summary>
    internal void RecordSuccess()
    {
        lock (_lock)
        {
            AdvanceWindow();
            Interlocked.Increment(ref _buckets[_currentBucketIndex].Successes);
        }
    }

    /// <summary>
    /// Records a failed execution.
    /// </summary>
    internal void RecordFailure()
    {
        lock (_lock)
        {
            AdvanceWindow();
            Interlocked.Increment(ref _buckets[_currentBucketIndex].Failures);
        }
    }

    /// <summary>
    /// Gets the current failure ratio and total throughput across all active buckets.
    /// </summary>
    /// <param name="totalCount">The total number of executions in the window.</param>
    /// <returns>The failure ratio clamped to [0.0, 1.0], or 0.0 if there are no executions.</returns>
    internal double GetFailureRatio(out int totalCount)
    {
        lock (_lock)
        {
            AdvanceWindow();

            var successes = 0;
            var failures = 0;

            for (var i = 0; i < BucketCount; i++)
            {
                successes += _buckets[i].Successes;
                failures += _buckets[i].Failures;
            }

            totalCount = successes + failures;
            if (totalCount == 0)
            {
                return 0.0;
            }

            var ratio = (double)failures / totalCount;
            return Math.Clamp(ratio, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Resets all buckets to zero. Called when transitioning to Closed state.
    /// Must be called under the circuit breaker's lock.
    /// </summary>
    internal void Reset()
    {
        lock (_lock)
        {
            for (var i = 0; i < BucketCount; i++)
            {
                _buckets[i].Successes = 0;
                _buckets[i].Failures = 0;
            }

            _currentBucketStartTimestamp = _timeProvider.GetTimestamp();
            _currentBucketIndex = 0;
        }
    }

    /// <summary>
    /// Advances the window by rotating expired buckets. Must be called under _lock.
    /// </summary>
    private void AdvanceWindow()
    {
        var elapsed = _timeProvider.GetElapsedTime(_currentBucketStartTimestamp);
        if (elapsed < _bucketDuration)
        {
            return;
        }

        var bucketsToAdvance = (int)(elapsed / _bucketDuration);
        if (bucketsToAdvance >= BucketCount)
        {
            for (var i = 0; i < BucketCount; i++)
            {
                _buckets[i].Successes = 0;
                _buckets[i].Failures = 0;
            }

            _currentBucketStartTimestamp = _timeProvider.GetTimestamp();
            _currentBucketIndex = 0;
            return;
        }

        for (var i = 0; i < bucketsToAdvance; i++)
        {
            _currentBucketIndex = (_currentBucketIndex + 1) % BucketCount;
            _buckets[_currentBucketIndex].Successes = 0;
            _buckets[_currentBucketIndex].Failures = 0;
        }

        _currentBucketStartTimestamp = _timeProvider.GetTimestamp();
    }

    private sealed class Bucket
    {
        public int Successes;
        public int Failures;
    }
}
