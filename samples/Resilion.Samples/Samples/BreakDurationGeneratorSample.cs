namespace Resilion.Samples;

/// <summary>
/// Exponential break-duration backoff: each time the circuit trips again, it stays open longer
/// than the last time — via <c>BreakDurationGenerator</c>, new in this version.
/// </summary>
public static class BreakDurationGeneratorSample
{
    public static async Task RunAsync()
    {
        var pipeline = Pipeline.Create(b => b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatioThreshold = 0.5,
            MinimumThroughput = 1,
            BreakDuration = TimeSpan.FromMilliseconds(100), // Base duration, passed to the generator.
            BreakDurationGenerator = args =>
            {
                // Doubles each trip: 100ms, 200ms, 400ms, ... capped at 1s.
                var backoff = args.CurrentBreakDuration * Math.Pow(2, args.FailureCount - 1);
                var capped = backoff > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : backoff;
                Console.WriteLine($"   Trip #{args.FailureCount}: breaking for {capped.TotalMilliseconds:F0}ms");
                return capped;
            },
        }));

        for (var trip = 1; trip <= 3; trip++)
        {
            // Trip the circuit.
            try
            {
                await pipeline.ExecuteAsync<string>(ct => throw new InvalidOperationException("down"));
            }
            catch (InvalidOperationException)
            {
                // Expected — this is what trips the circuit.
            }

            // Confirm it's open, then wait long enough for the generator's growing duration.
            try
            {
                await pipeline.ExecuteAsync(ct => new ValueTask<string>("ok"));
            }
            catch (CircuitBrokenException ex)
            {
                Console.WriteLine($"   Rejected, retry after {ex.RetryAfter.TotalMilliseconds:F0}ms");
                await Task.Delay(ex.RetryAfter + TimeSpan.FromMilliseconds(20));
            }
        }
    }
}
