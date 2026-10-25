namespace Resilion.Http.Internal;

/// <summary>
/// Captures the root <see cref="IServiceProvider"/> so a pipeline built lazily can resolve
/// services without capturing a scoped provider.
/// </summary>
/// <remarks>
/// <see cref="System.Net.Http.IHttpClientFactory"/> builds handler chains inside a dedicated scope
/// whose lifetime is the handler's — roughly two minutes by default. Capturing that provider in a
/// singleton pipeline would be a captive dependency that outlives its scope. Registering this as a
/// singleton means it is activated in the root scope, so the provider it receives is the root one.
/// </remarks>
internal sealed class HttpResilienceRootProvider
{
    // Public, though the type is internal: the DI container activates by reflection and requires
    // a public constructor. Internal visibility of the type is what keeps this off the public API.
    public HttpResilienceRootProvider(IServiceProvider services) => Services = services;

    internal IServiceProvider Services { get; }
}

/// <summary>
/// A one-field holder bridging registration time to resolution time.
/// </summary>
/// <remarks>
/// <c>ResiliencePipelineRegistry</c>'s factory signature has no <see cref="IServiceProvider"/>, so
/// a pipeline that needs one has to reach it some other way. The handler factory assigns
/// <see cref="Services"/> immediately before the <c>GetPipeline</c> call that can trigger the lazy
/// build, which is the only thing that reads it — so the ordering is deterministic rather than
/// hopeful. Concurrent first-resolution from two clients writes the same root provider, making the
/// race benign.
/// </remarks>
internal sealed class RootProviderAccessor
{
    internal IServiceProvider? Services { get; set; }
}

/// <summary>
/// Marks a pipeline key as already claimed, so a duplicate registration is caught at registration
/// time rather than surfacing later as a confusing failure from the registry.
/// </summary>
internal sealed class ResilionHttpPipelineMarker
{
    internal ResilionHttpPipelineMarker(string pipelineKey) => PipelineKey = pipelineKey;

    internal string PipelineKey { get; }
}
