using System.Threading.RateLimiting;
using Resilion.RateLimiting;

namespace Resilion.Samples;

/// <summary>
/// A <see cref="ConcurrencyLimiter"/> wrapped in the canonical pipeline position (outermost —
/// shed load before spending any other resilience work on a call that won't be admitted).
/// </summary>
public static class RateLimiterSample
{
    public static async Task RunAsync()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 2,
            QueueLimit = 0,
        });

        Action<OnRateLimitRejectedEvent> onRejected = e =>
            Console.WriteLine("   Rejected: rate limit exceeded");

        var pipeline = Pipeline.Create(b => b
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = limiter,
                OnRejected = onRejected,
            })
            .AddTimeout(TimeSpan.FromSeconds(10)));

        // Launch 4 concurrent calls against a limiter that only permits 2 at a time.
        var tasks = Enumerable.Range(1, 4).Select(async i =>
        {
            try
            {
                var result = await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Delay(200, ct);
                    return $"call-{i}-ok";
                });
                Console.WriteLine($"   {result}");
            }
            catch (RateLimitRejectedException)
            {
                Console.WriteLine($"   call-{i} rejected");
            }
        });

        await Task.WhenAll(tasks);
    }
}
