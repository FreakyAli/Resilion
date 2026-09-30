using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Resilion.Http.Tests;

/// <summary>
/// Builds a real <see cref="IHttpClientFactory"/> graph over a stub primary handler, so tests
/// exercise the same registration and resolution path an application would.
/// </summary>
internal static class TestClientFactory
{
    internal const string ClientName = "api";

    internal static (ServiceProvider Provider, HttpClient Client) Build(
        StubHttpMessageHandler stub,
        Action<IHttpClientBuilder> configureResilience,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var builder = services.AddHttpClient(ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => stub);

        configureResilience(builder);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        return (provider, client);
    }
}
