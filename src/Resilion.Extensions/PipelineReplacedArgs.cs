namespace Resilion.Extensions;

/// <summary>
/// Describes a pipeline that has just been evicted from a
/// <see cref="ResiliencePipelineRegistry{TKey}"/> by an invalidation.
/// </summary>
/// <typeparam name="TKey">The registry's key type.</typeparam>
/// <param name="Key">The key whose pipeline was evicted.</param>
/// <param name="ResultType">
/// The pipeline's result type, or <see langword="null"/> for a non-generic pipeline.
/// </param>
/// <param name="ReplacedPipeline">
/// The evicted instance. <see cref="IAsyncDisposable"/> is the common supertype of
/// <see cref="Pipeline"/> and <see cref="Pipeline{TResult}"/>.
/// </param>
/// <remarks>
/// The registry does <strong>not</strong> dispose an evicted pipeline, because it cannot know when
/// the last in-flight execution on it has finished — that would require ref-counting every execute
/// path. Disposal is therefore the caller's decision, and this is how the caller learns there is
/// something to decide about.
/// <para>
/// Disposing it immediately is usually wrong: executions already running on the old pipeline
/// complete on the old pipeline. Wait, or simply let it be collected, which is safe while no
/// strategy owns an unmanaged resource.
/// </para>
/// </remarks>
public readonly record struct PipelineReplacedArgs<TKey>(
    TKey Key,
    Type? ResultType,
    IAsyncDisposable ReplacedPipeline)
    where TKey : notnull;
