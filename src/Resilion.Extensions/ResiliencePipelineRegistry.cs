using System.Collections.Concurrent;

namespace Resilion.Extensions;

/// <summary>
/// A thread-safe registry of named resilience pipelines. Pipelines are created lazily
/// on first access and cached for the lifetime of the registry.
/// </summary>
/// <typeparam name="TKey">The key type for pipeline lookup. Typically <see cref="string"/>.</typeparam>
public sealed class ResiliencePipelineRegistry<TKey> : IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Pipeline>> _pipelines = new();
    private readonly ConcurrentDictionary<(TKey, Type), Lazy<object>> _typedPipelines = new();
    private readonly ConcurrentDictionary<TKey, Func<PipelineBuilder, PipelineBuilder>> _factories = new();
    private readonly ConcurrentDictionary<(TKey, Type), object> _typedFactories = new();

    /// <summary>
    /// Registers a factory for a named pipeline. The factory is invoked lazily on first access.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <param name="configure">A delegate that configures the pipeline builder.</param>
    /// <exception cref="ArgumentException">A pipeline with the same key is already registered.</exception>
    public void RegisterPipeline(TKey key, Action<PipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!_factories.TryAdd(key, builder => { configure(builder); return builder; }))
        {
            throw new ArgumentException($"A pipeline with key '{key}' is already registered.", nameof(key));
        }
    }

    /// <summary>
    /// Registers a factory for a named typed pipeline.
    /// </summary>
    /// <typeparam name="TResult">The result type for the pipeline.</typeparam>
    /// <param name="key">The pipeline name.</param>
    /// <param name="configure">A delegate that configures the typed pipeline builder.</param>
    public void RegisterPipeline<TResult>(TKey key, Action<PipelineBuilder<TResult>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var compositeKey = (key, typeof(TResult));
        if (!_typedFactories.TryAdd(compositeKey, configure))
        {
            throw new ArgumentException(
                $"A pipeline with key '{key}' and result type '{typeof(TResult).Name}' is already registered.",
                nameof(key));
        }
    }

    /// <summary>
    /// Gets or creates the pipeline registered under the specified key.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <returns>The cached pipeline instance.</returns>
    /// <exception cref="KeyNotFoundException">No pipeline is registered with the specified key.</exception>
    public Pipeline GetPipeline(TKey key)
    {
        var lazy = _pipelines.GetOrAdd(key, _ => new Lazy<Pipeline>(() =>
        {
            if (!_factories.TryGetValue(key, out var factory))
            {
                throw new KeyNotFoundException($"No pipeline registered with key '{key}'.");
            }

            var builder = new PipelineBuilder();
            factory(builder);
            return builder.Build();
        }));

        return lazy.Value;
    }

    /// <summary>
    /// Gets or creates the typed pipeline registered under the specified key.
    /// </summary>
    /// <typeparam name="TResult">The result type for the pipeline.</typeparam>
    /// <param name="key">The pipeline name.</param>
    /// <returns>The cached typed pipeline instance.</returns>
    public Pipeline<TResult> GetPipeline<TResult>(TKey key)
    {
        var compositeKey = (key, typeof(TResult));
        var lazy = _typedPipelines.GetOrAdd(compositeKey, _ => new Lazy<object>(() =>
        {
            if (!_typedFactories.TryGetValue(compositeKey, out var factory))
            {
                throw new KeyNotFoundException(
                    $"No pipeline registered with key '{key}' and result type '{typeof(TResult).Name}'.");
            }

            var configure = (Action<PipelineBuilder<TResult>>)factory;
            var builder = new PipelineBuilder<TResult>();
            configure(builder);
            return (object)builder.Build();
        }));

        return (Pipeline<TResult>)lazy.Value;
    }

    /// <summary>
    /// Attempts to get a pipeline registered under the specified key.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <param name="pipeline">The pipeline, if found.</param>
    /// <returns><c>true</c> if the pipeline exists; <c>false</c> otherwise.</returns>
    public bool TryGetPipeline(TKey key, out Pipeline? pipeline)
    {
        if (_factories.ContainsKey(key))
        {
            pipeline = GetPipeline(key);
            return true;
        }

        pipeline = null;
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var lazy in _pipelines.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        foreach (var lazy in _typedPipelines.Values)
        {
            if (lazy.IsValueCreated && lazy.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _pipelines.Clear();
        _typedPipelines.Clear();
    }
}
