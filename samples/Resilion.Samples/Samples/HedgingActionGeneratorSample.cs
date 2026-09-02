namespace Resilion.Samples;

/// <summary>
/// Hedging with a per-attempt <c>ActionGenerator</c> — the primary attempt calls one endpoint,
/// the hedged attempt calls a different one (a common pattern: primary region + failover region).
/// </summary>
public static class HedgingActionGeneratorSample
{
    private static ValueTask<string> CallPrimaryEndpoint(CancellationToken ct)
        // Simulates a slow primary endpoint.
        => SlowCallAsync("primary-region", TimeSpan.FromMilliseconds(400), ct);

    private static ValueTask<string> CallFailoverEndpoint(CancellationToken ct)
        // Simulates a fast failover endpoint.
        => SlowCallAsync("failover-region", TimeSpan.FromMilliseconds(50), ct);

    private static async ValueTask<string> SlowCallAsync(string endpoint, TimeSpan delay, CancellationToken ct)
    {
        await Task.Delay(delay, ct);
        return $"response from {endpoint}";
    }

    public static async Task RunAsync()
    {
        Action<OnHedgingEvent<string>> onHedging = e =>
            Console.WriteLine($"   Launching hedged attempt #{e.AttemptNumber} (failover)");

        var pipeline = Pipeline.Create<string>(b => b.AddHedging(new HedgingStrategyOptions<string>
        {
            MaxHedgedAttempts = 2,
            HedgingDelay = TimeSpan.FromMilliseconds(100), // Latency mode: give primary a head start.
            OnHedging = onHedging,
            ActionGenerator = ctx => ctx.AttemptNumber switch
            {
                0 => CallPrimaryEndpoint,
                1 => CallFailoverEndpoint,
                _ => null,
            },
        }));

        // The delegate passed here is never called — ActionGenerator supplies every attempt.
        var result = await pipeline.ExecuteAsync(ct =>
            throw new InvalidOperationException("unreachable — ActionGenerator handles all attempts"));

        Console.WriteLine($"   Result: {result}");
    }
}
