using Microsoft.Extensions.DependencyInjection;
using Resilion.Extensions;

namespace Resilion.Samples;

/// <summary>
/// Shows the real-world DI registration pattern: <c>AddResilienceServices()</c> +
/// <c>AddResiliencePipeline()</c>, then resolving pipelines via <see cref="IPipelineProvider{TKey}"/>
/// — the read-only interface consumers should depend on instead of the full registry.
/// </summary>
public static class DependencyInjectionSample
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection();

        services.AddResiliencePipeline("http-api", b => b
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = RetryDelay.Exponential(TimeSpan.FromMilliseconds(100)),
            })
            .AddTimeout(TimeSpan.FromSeconds(10)));

        await using var provider = services.BuildServiceProvider();

        // Consumers only need the read-only provider interface, not the full registry
        // (which also exposes registration) — see IPipelineProvider<TKey>.
        var pipelineProvider = provider.GetRequiredService<IPipelineProvider<string>>();
        var pipeline = pipelineProvider.GetPipeline("http-api");

        var result = await pipeline.ExecuteAsync(
            static (state, ct) => new ValueTask<string>($"Response from {state}"),
            "http-api");

        Console.WriteLine($"   Result: {result}");
    }
}
