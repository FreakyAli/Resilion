using System.Net;

namespace Resilion.Samples;

/// <summary>
/// The #1 thing people search for: retrying on specific HTTP status codes using a typed
/// pipeline. <c>Pipeline.Create&lt;HttpResponseMessage&gt;</c> lets <c>ShouldHandle</c> inspect
/// the response itself, not just exceptions.
/// </summary>
public static class TypedRetrySample
{
    public static async Task RunAsync()
    {
        var attempt = 0;

        var pipeline = Pipeline.Create<HttpResponseMessage>(b => b.AddRetry(
            new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100)),
                // Retry on 5xx and 429 (Too Many Requests) — the classic transient HTTP set.
                ShouldHandle = outcome =>
                    outcome.Exception is not null and not OperationCanceledException
                    || (outcome.TryGetResult(out var response)
                        && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)),
            }));

        var result = await pipeline.ExecuteAsync(ct =>
        {
            attempt++;
            // Simulates a flaky endpoint: 503 twice, then 200.
            var statusCode = attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(statusCode));
        });

        Console.WriteLine($"   Result: {result.StatusCode} (after {attempt} attempts)");
        result.Dispose();
    }
}
