using System.Collections.Concurrent;

namespace Resilion.Extensions;

/// <summary>
/// A thread-safe registry of named resilience pipelines. Pipelines are created lazily
/// on first access and cached for the lifetime of the registry.
/// </summary>
/// <typeparam name="TKey">The key type for pipeline lookup. Typically <see cref="string"/>.</typeparam>
public sealed class ResiliencePipelineRegistry<TKey> : IPipelineProvider<TKey>, IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Pipeline>> _pipelines = new();
    private readonly ConcurrentDictionary<(TKey, Type), Lazy<object>> _typedPipelines = new();
    private readonly ConcurrentDictionary<TKey, Func<PipelineBuilder, PipelineBuilder>> _factories = new();
    private readonly ConcurrentDictionary<(TKey, Type), object> _typedFactories = new();
    private readonly ConcurrentBag<IDisposable> _reloadSubscriptions = [];

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
        // Validate factory exists before creating Lazy to prevent caching faulted entries
        if (!_factories.TryGetValue(key, out var factory))
        {
            throw new KeyNotFoundException($"No pipeline registered with key '{key}'.");
        }

        var lazy = _pipelines.GetOrAdd(key, _ => new Lazy<Pipeline>(() =>
        {
            var builder = new PipelineBuilder { Name = key?.ToString() };
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

        // Validate factory exists before creating Lazy to prevent caching faulted entries
        if (!_typedFactories.TryGetValue(compositeKey, out var factory))
        {
            throw new KeyNotFoundException(
                $"No pipeline registered with key '{key}' and result type '{typeof(TResult).Name}'.");
        }

        var lazy = _typedPipelines.GetOrAdd(compositeKey, _ => new Lazy<object>(() =>
        {
            var configure = (Action<PipelineBuilder<TResult>>)factory;
            var builder = new PipelineBuilder<TResult> { Name = key?.ToString() };
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

    /// <summary>
    /// Attempts to get the typed pipeline registered under the specified key and result type.
    /// </summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <param name="key">The pipeline name.</param>
    /// <param name="pipeline">The pipeline, if found.</param>
    /// <returns><c>true</c> if the pipeline exists; <c>false</c> otherwise.</returns>
    /// <remarks>
    /// The typed counterpart of <see cref="TryGetPipeline(TKey, out Resilion.Pipeline?)"/>. Not on
    /// <see cref="IPipelineProvider{TKey}"/>: adding a member to a public interface would break
    /// external implementers, so the interface gains it in a future major version.
    /// </remarks>
    public bool TryGetPipeline<TResult>(TKey key, out Pipeline<TResult>? pipeline)
    {
        if (_typedFactories.ContainsKey((key, typeof(TResult))))
        {
            pipeline = GetPipeline<TResult>(key);
            return true;
        }

        pipeline = null;
        return false;
    }

    /// <summary>
    /// Gets or sets a callback invoked when an invalidation evicts a pipeline that had already
    /// been built.
    /// </summary>
    /// <remarks>
    /// The registry never disposes an evicted pipeline — see
    /// <see cref="PipelineReplacedArgs{TKey}"/> for why, and what your options are.
    /// </remarks>
    public Action<PipelineReplacedArgs<TKey>>? OnPipelineReplaced { get; set; }

    /// <summary>
    /// Drops the cached pipeline for <paramref name="key"/>, so the next
    /// <see cref="GetPipeline(TKey)"/> rebuilds it from its registered factory.
    /// </summary>
    /// <param name="key">The pipeline name.</param>
    /// <returns>
    /// <c>true</c> if a cached entry was removed; <c>false</c> if nothing was cached — either the
    /// key is unregistered, or its pipeline had not been built yet, in which case the next access
    /// already builds a current one.
    /// </returns>
    /// <remarks>
    /// The registered factory is untouched, which is what makes reload work: builders are
    /// single-use, and the factory constructs a fresh one on every build.
    /// <para>
    /// In-flight executions complete on the old pipeline. It is immutable and the executing frame
    /// holds it on its own stack, so nothing tears.
    /// </para>
    /// <para>
    /// Eventually consistent: a concurrent invalidate and get can return a pipeline built from
    /// options that were current microseconds ago. Locking to prevent that would serialize every
    /// resolve for no practical gain.
    /// </para>
    /// </remarks>
    public bool InvalidatePipeline(TKey key)
    {
        if (!_pipelines.TryRemove(key, out var lazy))
        {
            return false;
        }

        if (lazy.IsValueCreated)
        {
            OnPipelineReplaced?.Invoke(new PipelineReplacedArgs<TKey>(key, null, lazy.Value));
        }

        return true;
    }

    /// <summary>
    /// Drops the cached typed pipeline for <paramref name="key"/> and
    /// <typeparamref name="TResult"/>, so the next <see cref="GetPipeline{TResult}(TKey)"/>
    /// rebuilds it.
    /// </summary>
    /// <typeparam name="TResult">The pipeline's result type.</typeparam>
    /// <param name="key">The pipeline name.</param>
    /// <returns><c>true</c> if a cached entry was removed; <c>false</c> otherwise.</returns>
    /// <remarks>
    /// Typed pipelines are keyed by name <em>and</em> result type, so this affects only the entry
    /// for <typeparamref name="TResult"/>. Same semantics as
    /// <see cref="InvalidatePipeline(TKey)"/> otherwise.
    /// </remarks>
    public bool InvalidatePipeline<TResult>(TKey key)
    {
        var compositeKey = (key, typeof(TResult));
        if (!_typedPipelines.TryRemove(compositeKey, out var lazy))
        {
            return false;
        }

        if (lazy.IsValueCreated && lazy.Value is IAsyncDisposable disposable)
        {
            OnPipelineReplaced?.Invoke(
                new PipelineReplacedArgs<TKey>(key, typeof(TResult), disposable));
        }

        return true;
    }

    /// <summary>
    /// Takes ownership of a change-notification subscription, disposing it with the registry.
    /// </summary>
    internal void AddReloadSubscription(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _reloadSubscriptions.Add(subscription);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Stop listening before disposing pipelines, so a change arriving mid-teardown cannot
        // rebuild a pipeline into a registry that is going away.
        while (_reloadSubscriptions.TryTake(out var subscription))
        {
            subscription.Dispose();
        }

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
