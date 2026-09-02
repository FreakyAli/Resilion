namespace Resilion;

/// <summary>
/// Represents a fallback action that produces a substitute result. Supports three forms
/// via implicit conversion: a constant value, a synchronous factory, or an async factory.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <example>
/// <code>
/// // Constant value — simplest case
/// FallbackAction = "default"
///
/// // Sync factory — compute from the failure
/// FallbackAction = ctx => ComputeDefault(ctx.Exception)
///
/// // Async factory — call a cache or secondary service
/// FallbackAction = async ctx => await cache.GetAsync("fallback-key")
/// </code>
/// </example>
public readonly struct FallbackAction<TResult>
{
    private readonly TResult? _value;
    private readonly Func<FallbackContext<TResult>, TResult>? _syncFactory;
    private readonly Func<FallbackContext<TResult>, ValueTask<TResult>>? _asyncFactory;
    private readonly FallbackKind _kind;

    private FallbackAction(TResult? value, Func<FallbackContext<TResult>, TResult>? syncFactory,
        Func<FallbackContext<TResult>, ValueTask<TResult>>? asyncFactory, FallbackKind kind)
    {
        _value = value;
        _syncFactory = syncFactory;
        _asyncFactory = asyncFactory;
        _kind = kind;
    }

    /// <summary>
    /// Gets a value indicating whether this action has been assigned.
    /// </summary>
    public bool HasValue => _kind != FallbackKind.None;

    /// <summary>
    /// Produces the fallback result asynchronously.
    /// </summary>
    internal async ValueTask<TResult> ExecuteAsync(FallbackContext<TResult> context)
    {
        return _kind switch
        {
            FallbackKind.Constant => _value!,
            FallbackKind.Sync => _syncFactory!(context),
            FallbackKind.Async => await _asyncFactory!(context).ConfigureAwait(false),
            _ => throw new InvalidOperationException("FallbackAction has not been configured."),
        };
    }

    /// <summary>
    /// Produces the fallback result synchronously.
    /// </summary>
    internal TResult Execute(FallbackContext<TResult> context)
    {
        if (_kind == FallbackKind.Async)
        {
            // Copy to local to avoid struct 'this' capture in lambda.
            var factory = _asyncFactory!;
            return Task.Run(() => factory(context).AsTask()).GetAwaiter().GetResult();
        }

        return _kind switch
        {
            FallbackKind.Constant => _value!,
            FallbackKind.Sync => _syncFactory!(context),
            _ => throw new InvalidOperationException("FallbackAction has not been configured."),
        };
    }

    /// <summary>
    /// Implicitly converts a constant value to a <see cref="FallbackAction{TResult}"/>.
    /// </summary>
    public static implicit operator FallbackAction<TResult>(TResult value)
        => new(value, null, null, FallbackKind.Constant);

    /// <summary>
    /// Implicitly converts a synchronous factory to a <see cref="FallbackAction{TResult}"/>.
    /// </summary>
    public static implicit operator FallbackAction<TResult>(Func<FallbackContext<TResult>, TResult> factory)
        => new(default, factory, null, FallbackKind.Sync);

    /// <summary>
    /// Implicitly converts an async factory to a <see cref="FallbackAction{TResult}"/>.
    /// </summary>
    public static implicit operator FallbackAction<TResult>(Func<FallbackContext<TResult>, ValueTask<TResult>> factory)
        => new(default, null, factory, FallbackKind.Async);

    private enum FallbackKind : byte
    {
        None,
        Constant,
        Sync,
        Async,
    }
}

/// <summary>
/// Context provided to fallback action factories, containing information about the failure.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="Outcome">The failed outcome that triggered the fallback.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct FallbackContext<TResult>(
    Outcome<TResult> Outcome,
    ResilienceContext Context)
{
    /// <summary>
    /// Gets the exception from the failed outcome, if any.
    /// </summary>
    public Exception? Exception => Outcome.Exception;
}
