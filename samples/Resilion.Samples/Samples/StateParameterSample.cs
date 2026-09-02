namespace Resilion.Samples;

/// <summary>
/// The <c>Execute&lt;TResult, TState&gt;</c> / <c>ExecuteAsync&lt;TResult, TState&gt;</c> overloads
/// pass state explicitly instead of capturing it in a closure. Combined with a <c>static</c>
/// lambda, this avoids allocating a display-class object per call — worth doing on a hot path
/// where the other overloads' closure allocation (small, but non-zero) actually shows up.
/// </summary>
public static class StateParameterSample
{
    private readonly record struct RequestState(string Url, int TimeoutSeconds);

    public static async Task RunAsync()
    {
        var pipeline = Pipeline.Create(b => b.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            Delay = RetryDelay.None,
        }));

        // Without TState: the lambda captures `url` and `timeoutSeconds` from the enclosing
        // scope, which the compiler implements as a heap-allocated closure object.
        var url = "https://api.example.com";
        var timeoutSeconds = 10;
        var withClosure = await pipeline.ExecuteAsync(ct =>
            new ValueTask<string>($"GET {url} (timeout {timeoutSeconds}s)"));
        Console.WriteLine($"   {withClosure}");

        // With TState: a `static` lambda can't capture anything, so the compiler can't allocate
        // a closure — `state` is passed as an ordinary parameter instead.
        var withState = await pipeline.ExecuteAsync(
            static (state, ct) => new ValueTask<string>($"GET {state.Url} (timeout {state.TimeoutSeconds}s)"),
            new RequestState("https://api.example.com", 10));
        Console.WriteLine($"   {withState}");
    }
}
