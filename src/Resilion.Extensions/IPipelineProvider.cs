namespace Resilion.Extensions;

/// <summary>
/// A read-only view over a set of named resilience pipelines. Consumers that only need to
/// retrieve pipelines should depend on this interface rather than
/// <see cref="ResiliencePipelineRegistry{TKey}"/>, which also exposes registration.
/// </summary>
/// <typeparam name="TKey">The key type for pipeline lookup. Typically <see cref="string"/>.</typeparam>
public interface IPipelineProvider<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Gets or creates the pipeline registered under the specified key.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <returns>The cached pipeline instance.</returns>
    /// <exception cref="KeyNotFoundException">No pipeline is registered with the specified key.</exception>
    Pipeline GetPipeline(TKey key);

    /// <summary>
    /// Gets or creates the typed pipeline registered under the specified key.
    /// </summary>
    /// <typeparam name="TResult">The result type for the pipeline.</typeparam>
    /// <param name="key">The pipeline name.</param>
    /// <returns>The cached typed pipeline instance.</returns>
    /// <exception cref="KeyNotFoundException">No pipeline is registered with the specified key and result type.</exception>
    Pipeline<TResult> GetPipeline<TResult>(TKey key);

    /// <summary>
    /// Attempts to get a pipeline registered under the specified key.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <param name="pipeline">The pipeline, if found.</param>
    /// <returns><c>true</c> if the pipeline exists; <c>false</c> otherwise.</returns>
    bool TryGetPipeline(TKey key, out Pipeline? pipeline);
}
